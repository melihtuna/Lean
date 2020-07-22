﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); 
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using com.fxcm.fix;
using com.fxcm.fix.pretrade;
using NodaTime;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Logging;
using QuantConnect.Packets;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.Fxcm
{
    /// <summary>
    /// FXCM brokerage - implementation of IDataQueueHandler interface
    /// </summary>
    public partial class FxcmBrokerage
    {
        private readonly HashSet<Symbol> _subscribedSymbols = new HashSet<Symbol>();

        #region IDataQueueHandler implementation

        /// <summary>
        /// Adds the specified symbols to the subscription
        /// </summary>
        /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
        /// <param name="newDataAvailableHandler">fired when new data are ready</param>
        /// <returns></returns>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            Subscribe(new[] { dataConfig.Symbol });

            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            return enumerator;
        }

        /// <summary>
        /// Adds the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be added keyed by SecurityType</param>
        private void Subscribe(IEnumerable<Symbol> symbols)
        {
            var symbolsToSubscribe = (from symbol in symbols 
                                      where !_subscribedSymbols.Contains(symbol) && CanSubscribe(symbol)
                                      select symbol).ToList();
            if (symbolsToSubscribe.Count == 0)
                return;

            Log.Trace("FxcmBrokerage.Subscribe(): {0}", string.Join(",", symbolsToSubscribe));

            var request = new MarketDataRequest();
            foreach (var symbol in symbolsToSubscribe)
            {
                TradingSecurity fxcmSecurity;
                if (_fxcmInstruments.TryGetValue(_symbolMapper.GetBrokerageSymbol(symbol), out fxcmSecurity))
                {
                    request.addRelatedSymbol(fxcmSecurity);

                    // cache exchange time zone for symbol
                    DateTimeZone exchangeTimeZone;
                    if (!_symbolExchangeTimeZones.TryGetValue(symbol, out exchangeTimeZone))
                    {
                        exchangeTimeZone = MarketHoursDatabase.FromDataFolder().GetExchangeHours(Market.FXCM, symbol, symbol.SecurityType).TimeZone;
                        _symbolExchangeTimeZones.Add(symbol, exchangeTimeZone);
                    }

                }
            }
            request.setSubscriptionRequestType(SubscriptionRequestTypeFactory.SUBSCRIBE);
            request.setMDEntryTypeSet(MarketDataRequest.MDENTRYTYPESET_ALL);

            lock (_locker)
            {
                _gateway.sendMessage(request);
            }

            foreach (var symbol in symbolsToSubscribe)
            {
                _subscribedSymbols.Add(symbol);
            }
        }

        /// <summary>
        /// Removes the specified symbols to the subscription
        /// </summary>
        /// <param name="dataConfig">Subscription config to be removed</param>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            Unsubscribe(new Symbol[] { dataConfig.Symbol });
        }

        /// <summary>
        /// Removes the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be removed keyed by SecurityType</param>
        public void Unsubscribe(IEnumerable<Symbol> symbols)
        {
            var symbolsToUnsubscribe = (from symbol in symbols 
                                        where _subscribedSymbols.Contains(symbol) 
                                        select symbol).ToList();
            if (symbolsToUnsubscribe.Count == 0)
                return;

            Log.Trace("FxcmBrokerage.Unsubscribe(): {0}", string.Join(",", symbolsToUnsubscribe));

            var request = new MarketDataRequest();
            foreach (var symbol in symbolsToUnsubscribe)
            {
                request.addRelatedSymbol(_fxcmInstruments[_symbolMapper.GetBrokerageSymbol(symbol)]);
            }
            request.setSubscriptionRequestType(SubscriptionRequestTypeFactory.UNSUBSCRIBE);
            request.setMDEntryTypeSet(MarketDataRequest.MDENTRYTYPESET_ALL);

            lock (_locker)
            {
                _gateway.sendMessage(request);
            }

            foreach (var symbol in symbolsToUnsubscribe)
            {
                _subscribedSymbols.Remove(symbol);
            }
        }

        /// <summary>
        /// Returns true if this brokerage supports the specified symbol
        /// </summary>
        private static bool CanSubscribe(Symbol symbol)
        {
            // ignore unsupported security types
            if (symbol.ID.SecurityType != SecurityType.Forex && symbol.ID.SecurityType != SecurityType.Cfd)
                return false;

            // ignore universe symbols
            return !symbol.Value.Contains("-UNIVERSE-");
        }

        #endregion

    }
}
