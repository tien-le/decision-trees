﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Bridge.IBLL.Data;
using Bridge.IBLL.Interfaces;
using Shared.DecisionTrees.DataStructure;
using MonthPeriodKey = System.Collections.Generic.KeyValuePair<string, string>;

namespace ForexTradeModel
{

    public class TradingModel
    {

        private readonly IStatisticsService _statisticsService;
        private readonly IForexMarketService _forexMarketService;
        private readonly IForexTradingAgentService _forexTradingAgentService;
        private readonly IForexTradingService _forexTradingService;
        private readonly List<string> _months;
        private bool _initialised;
        private bool _prepared;
        private bool _quantitiesSet;
        private bool _burnedOut;
        private double _initialBalance;
        private long _recordsProcessed;

        public TradingModel(
            IStatisticsService statisticsService,
            IForexMarketService forexMarketService,
            IForexTradingAgentService forexTradingAgentService,
            IForexTradingService forexTradingService)
        {
            _statisticsService = statisticsService;
            _forexMarketService = forexMarketService;
            _forexTradingAgentService = forexTradingAgentService;
            _forexTradingService = forexTradingService;
            _months = new List<string>();
            for (var i = 1; i <= 12; i++)
            {
                _months.Add(string.Format("{0:00}", i));
            }
            StatisticsSequences = new Dictionary<MonthPeriodKey, List<StatisticsSequenceDto>>();
        }

        public string FullForexTreesPath { get; private set; }
        public DecisionTreeAlgorithm Algorithm { get; private set; }
        public Dictionary<MonthPeriodKey, List<StatisticsSequenceDto>> StatisticsSequences { get; private set; }
        public double Balance { get; private set; }

        public void Initialize(string currency, string year, List<string> periods, int cases, string statisticsPath, string forexTreesPath)
        {
            FullForexTreesPath = Path.Combine(forexTreesPath, currency, year);
            _forexMarketService.SetForexTreesPath(FullForexTreesPath);

            foreach (var month in _months)
            {
                foreach (var period in periods)
                {
                    var fileName = string.Format("EURUSD_2014_{0}_{1}.csv", month, period);
                    var fullPath = Path.Combine(statisticsPath, fileName);
                    _statisticsService.ReadStatisticsData(fullPath);
                    _statisticsService.PrepareData();
                    _statisticsService.StatisticsSequence.RemoveAll(x => x.Cases < cases);

                    var sequence = new List<StatisticsSequenceDto>();
                    sequence.AddRange(_statisticsService.StatisticsSequence);
                    StatisticsSequences.Add(new MonthPeriodKey(month, period), sequence);

                    _statisticsService.ResetSequence();
                }
            }

            _initialised = true;
        }

        public void PrepareForAlgorithm(DecisionTreeAlgorithm algorithm)
        {
            Algorithm = algorithm;
            _forexTradingAgentService.Algorithm = algorithm;

            switch (algorithm)
            {
                case DecisionTreeAlgorithm.C45:
                    foreach (var sequence in StatisticsSequences.Select(sequenceElement => sequenceElement.Value))
                    {
                        sequence.Sort(delegate(StatisticsSequenceDto sequenceDtoOne, StatisticsSequenceDto sequenceDtoTwo)
                        {
                            var errorsOne = (double)sequenceDtoOne.C45Errors / sequenceDtoOne.Cases;
                            var errorsTwo = (double)sequenceDtoTwo.C45Errors / sequenceDtoTwo.Cases;
                            return errorsOne.CompareTo(errorsTwo);
                        });
                    }
                    break;
                case DecisionTreeAlgorithm.C50:
                    foreach (var sequence in StatisticsSequences.Select(sequenceElement => sequenceElement.Value))
                    {
                        sequence.Sort(delegate(StatisticsSequenceDto sequenceDtoOne, StatisticsSequenceDto sequenceDtoTwo)
                        {
                            var errorsOne = (double)sequenceDtoOne.C50Errors / sequenceDtoOne.Cases;
                            var errorsTwo = (double)sequenceDtoTwo.C50Errors / sequenceDtoTwo.Cases;
                            return errorsOne.CompareTo(errorsTwo);
                        });
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException("algorithm", algorithm, null);
            }
            _prepared = true;
        }

        public void SetModelQuantities(double initialBalance = 100000.0, double bidSize = 2000.0, double marginRatio = 0.02)
        {
            _quantitiesSet = true;
            Balance = initialBalance;
            _initialBalance = initialBalance;
            _forexTradingService.BidSize = bidSize;
            _forexTradingService.MarginRatio = marginRatio;
        }

        public void Trade()
        {
            CheckForInitialization();

            var algorithm = Algorithm.ToString();
            Console.WriteLine("Starting trading.");
            foreach (var sequenceElement in StatisticsSequences)
            {
                var elementKey = sequenceElement.Key;
                var month = elementKey.Key;
                var period = elementKey.Value;
                var sequences = sequenceElement.Value;
                _forexTradingAgentService.Period = period;
                _forexMarketService.Period = period;
                _forexTradingAgentService.StartingMonth = int.Parse(month);
                _forexMarketService.StartingMonth = int.Parse(month);
                Console.WriteLine("Chosen period {0} for month {1}", period, month);
                foreach (var sequence in sequences)
                {
                    Console.WriteLine("    Chosen chunk {0}. Trading.", sequence.Chunk);
                    _forexTradingAgentService.StartingChunk = sequence.Chunk;
                    _forexMarketService.StartingChunk = sequence.Chunk + 1;
                    _forexTradingAgentService.Initialize(FullForexTreesPath);
                    Balance = _initialBalance;
                    _recordsProcessed = 0;
                    while (!_forexMarketService.IsDone())
                    {
                        _recordsProcessed++;
                        var record = _forexMarketService.NextRecord();
                        var action = _forexTradingAgentService.ClassifyRecord(record);

                        switch (action)
                        {
                            case MarketAction.Buy:
                                if (Balance >= _forexTradingService.BidSize)
                                {
                                    Balance -= _forexTradingService.BidSize;
                                    _forexTradingService.PlaceBid(record, action);
                                }
                                else
                                {
                                    _forexTradingService.PlaceBid(record, MarketAction.Hold);
                                }
                                break;
                            case MarketAction.Sell:
                                if (_forexTradingService.BuyQuantities.Count < 1)
                                {
                                    _forexTradingService.PlaceBid(record, MarketAction.Hold);
                                    break;
                                }
                                _forexTradingService.PlaceBid(record, action);
                                Balance += _forexTradingService.BidSize + _forexTradingService.Profits.Last();
                                if (Balance < _forexTradingService.BidSize && _forexTradingService.BuyQuantities.Count == 0)
                                {
                                    _burnedOut = true;
                                }
                                break;
                            case MarketAction.Hold:
                                _forexTradingService.PlaceBid(record, action);
                                break;
                        }

                        if (_recordsProcessed % 100000 == 0)
                        {
                            Console.WriteLine("        Records processed: {0}.", _recordsProcessed);
                            Console.WriteLine("        Current balance: {0:0.00}.", Balance);
                        }

                        if (!_burnedOut)
                        {
                            continue;
                        }

                        Console.WriteLine("        Burned out.");
                        break;
                    }

                    if (_burnedOut)
                    {
                        Balance = _initialBalance;
                        _burnedOut = false;
                    }

                    Console.WriteLine("    Adding to repository.");
                    _forexTradingService.AddToRepository();
                    Console.WriteLine("    Added to repository.");

                    var path = Path.Combine(FullForexTreesPath, "TradingResults", string.Format("P{0}M{1}CH{2}A{3}.csv", period, month, sequence.Chunk, algorithm));

                    Console.WriteLine("    Commiting to repository.");
                    _forexTradingService.CommitToRepository(path);
                    Console.WriteLine("    Commited to repository, i.e. Saved.");
                    _forexTradingService.Clear();

                    Console.WriteLine("    Clearing market data.");
                    _forexMarketService.Clear();
                    Console.WriteLine("    Cleared market data.");
                    Console.WriteLine("    Chunk {0} completed.", sequence.Chunk);
                }
                Console.WriteLine("Period {0} for month {1} completed.", period, month);
            }

        }

        private void CheckForInitialization()
        {
            if (!_initialised)
            {
                throw new InvalidOperationException("You must initialize trading model at first.");
            }

            if (!_prepared)
            {
                throw new InvalidOperationException("You must prepare for chosen algorithm before trading.");
            }

            if (!_quantitiesSet)
            {
                throw new InvalidOperationException("You must set bid size, and margin to start modeling.");
            }
        }

    }

}
