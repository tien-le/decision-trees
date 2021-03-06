﻿#region Usings
using System.Configuration;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Implementation.DLL;
using Shared.DecisionTrees.DataStructure;
#endregion

namespace Tests.Integration
{
    [TestClass]
    public class TradingResultsRepositoryIntegrationTests
    {

        #region Private Fields
        private TradingResultsRepository _repository;
        #endregion

        #region TestInitialize
        [TestInitialize]
        public void TestInitialize()
        {
            _repository = new TradingResultsRepository();
        }
        #endregion

        #region TestSuite

        #region Add and Save Tests

        #region AddFourElementsAndSaveThem_TradingResults
        [TestMethod]
        [TestCategory("Integration")]
        public void AddFourElementsAndSaveThem_TradingResults()
        {
            var resultsFilePath = Path.Combine(ConfigurationManager.AppSettings["TestDataDirectory"], "TradingResults.csv");
            var builder = new StringBuilder();
            builder.AppendLine("QuantityBought,QuantitySold,Profit,ExecutedAction,CorrectAction");
            builder.AppendLine("10000,0,0,Buy,Buy");
            builder.AppendLine("0,10000,42.01,Sell,Sell");
            builder.AppendLine("0,0,0,Hold,Hold");
            builder.AppendLine("0,0,0,Hold,Buy");

            _repository.Add(new TradeLogRecord
            {
                CorrectAction = MarketAction.Buy,
                ExecutedAction = MarketAction.Buy,
                Profit = 0.0,
                QuantityBought = 10000.0,
                QuantitySold = 0.0
            });

            _repository.Add(new TradeLogRecord
            {
                CorrectAction = MarketAction.Sell,
                ExecutedAction = MarketAction.Sell,
                Profit = 42.01,
                QuantityBought = 0.0,
                QuantitySold = 10000.0
            });

            _repository.Add(new TradeLogRecord
            {
                CorrectAction = MarketAction.Hold,
                ExecutedAction = MarketAction.Hold,
                Profit = 0.0,
                QuantityBought = 0.0,
                QuantitySold = 0.0
            });

            _repository.Add(new TradeLogRecord
            {
                CorrectAction = MarketAction.Buy,
                ExecutedAction = MarketAction.Hold,
                Profit = 0.0,
                QuantityBought = 0.0,
                QuantitySold = 0.0
            });

            _repository.Save(resultsFilePath);

            var result = File.ReadAllText(resultsFilePath);

            Assert.AreEqual(builder.ToString(), result);
        }
        #endregion

        #endregion

        #endregion

    }
}
