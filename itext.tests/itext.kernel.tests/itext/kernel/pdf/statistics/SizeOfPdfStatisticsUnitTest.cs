/*
This file is part of the iText (R) project.
Copyright (c) 1998-2021 iText Group NV
Authors: iText Software.

This program is offered under a commercial and under the AGPL license.
For commercial licensing, contact us at https://itextpdf.com/sales.  For AGPL licensing, see below.

AGPL licensing:
This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/
using System;
using System.Collections;
using System.Collections.Generic;
using iText.IO.Util;
using iText.Kernel;
using iText.Kernel.Actions.Data;
using iText.Test;
using iText.Test.Attributes;

namespace iText.Kernel.Pdf.Statistics {
    public class SizeOfPdfStatisticsUnitTest : ExtendedITextTest {
        [NUnit.Framework.Test]
        public virtual void DefaultEventTest() {
            SizeOfPdfStatisticsEvent @event = new SizeOfPdfStatisticsEvent(0, ITextCoreProductData.GetInstance());
            NUnit.Framework.Assert.AreEqual(0, @event.GetAmountOfBytes());
            NUnit.Framework.Assert.AreEqual(JavaCollectionsUtil.SingletonList("pdfSize"), @event.GetStatisticsNames());
            NUnit.Framework.Assert.AreEqual(typeof(SizeOfPdfStatisticsAggregator), @event.CreateStatisticsAggregatorFromName
                ("pdfSize").GetType());
        }

        [NUnit.Framework.Test]
        public virtual void InvalidArgumentEventTest() {
            Exception exception = NUnit.Framework.Assert.Catch(typeof(ArgumentException), () => new SizeOfPdfStatisticsEvent
                (-1, ITextCoreProductData.GetInstance()));
            NUnit.Framework.Assert.AreEqual(PdfException.AmountOfBytesLessThanZero, exception.Message);
        }

        [NUnit.Framework.Test]
        [LogMessage(KernelLogMessageConstant.INVALID_STATISTICS_NAME)]
        public virtual void InvalidStatisticsNameEventTest() {
            SizeOfPdfStatisticsEvent @event = new SizeOfPdfStatisticsEvent(5, ITextCoreProductData.GetInstance());
            NUnit.Framework.Assert.IsNull(@event.CreateStatisticsAggregatorFromName("invalid name"));
        }

        [NUnit.Framework.Test]
        public virtual void AggregateEventTest() {
            SizeOfPdfStatisticsAggregator aggregator = new SizeOfPdfStatisticsAggregator();
            SizeOfPdfStatisticsEvent @event = new SizeOfPdfStatisticsEvent(100, ITextCoreProductData.GetInstance());
            aggregator.Aggregate(@event);
            @event = new SizeOfPdfStatisticsEvent(128 * 1024, ITextCoreProductData.GetInstance());
            aggregator.Aggregate(@event);
            @event = new SizeOfPdfStatisticsEvent(128 * 1024 + 1, ITextCoreProductData.GetInstance());
            aggregator.Aggregate(@event);
            @event = new SizeOfPdfStatisticsEvent(1024 * 1024, ITextCoreProductData.GetInstance());
            aggregator.Aggregate(@event);
            @event = new SizeOfPdfStatisticsEvent(100000000, ITextCoreProductData.GetInstance());
            aggregator.Aggregate(@event);
            @event = new SizeOfPdfStatisticsEvent(167972160, ITextCoreProductData.GetInstance());
            aggregator.Aggregate(@event);
            @event = new SizeOfPdfStatisticsEvent(999999999999L, ITextCoreProductData.GetInstance());
            aggregator.Aggregate(@event);
            Object aggregation = aggregator.RetrieveAggregation();
            NUnit.Framework.Assert.IsTrue(aggregation is IDictionary);
            IDictionary<String, AtomicLong> castedAggregation = (IDictionary<String, AtomicLong>)aggregation;
            NUnit.Framework.Assert.AreEqual(4, castedAggregation.Count);
            long numberOfPages = castedAggregation.Get("<128kb").Get();
            NUnit.Framework.Assert.AreEqual(2, numberOfPages);
            numberOfPages = castedAggregation.Get("128kb-1mb").Get();
            NUnit.Framework.Assert.AreEqual(2, numberOfPages);
            NUnit.Framework.Assert.IsNull(castedAggregation.Get("1mb-16mb"));
            numberOfPages = castedAggregation.Get("16mb-128mb").Get();
            NUnit.Framework.Assert.AreEqual(1, numberOfPages);
            numberOfPages = castedAggregation.Get("128mb+").Get();
            NUnit.Framework.Assert.AreEqual(2, numberOfPages);
        }

        [NUnit.Framework.Test]
        public virtual void NothingAggregatedTest() {
            SizeOfPdfStatisticsAggregator aggregator = new SizeOfPdfStatisticsAggregator();
            Object aggregation = aggregator.RetrieveAggregation();
            NUnit.Framework.Assert.IsTrue(aggregation is IDictionary);
            IDictionary<String, AtomicLong> castedAggregation = (IDictionary<String, AtomicLong>)aggregation;
            NUnit.Framework.Assert.IsTrue(castedAggregation.IsEmpty());
        }

        [NUnit.Framework.Test]
        public virtual void AggregateWrongEventTest() {
            SizeOfPdfStatisticsAggregator aggregator = new SizeOfPdfStatisticsAggregator();
            aggregator.Aggregate(new NumberOfPagesStatisticsEvent(200, ITextCoreProductData.GetInstance()));
            Object aggregation = aggregator.RetrieveAggregation();
            NUnit.Framework.Assert.IsTrue(aggregation is IDictionary);
            IDictionary<String, AtomicLong> castedAggregation = (IDictionary<String, AtomicLong>)aggregation;
            NUnit.Framework.Assert.IsTrue(castedAggregation.IsEmpty());
        }
    }
}