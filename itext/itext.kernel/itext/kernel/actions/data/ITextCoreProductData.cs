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
using iText.Kernel.Actions;

namespace iText.Kernel.Actions.Data {
    /// <summary>
    /// Stores an instance of
    /// <see cref="ProductData"/>
    /// related to iText core module.
    /// </summary>
    public class ITextCoreProductData {
        private const String CORE_PUBLIC_PRODUCT_NAME = "Core";

        private const String CORE_VERSION = "7.1.16-SNAPSHOT";

        private const int CORE_COPYRIGHT_SINCE = 1998;

        private const int CORE_COPYRIGHT_TO = 2021;

        private static readonly ProductData ITEXT_PRODUCT_DATA = new ProductData(CORE_PUBLIC_PRODUCT_NAME, ProductNameConstant
            .ITEXT_CORE, CORE_VERSION, CORE_COPYRIGHT_SINCE, CORE_COPYRIGHT_TO);

        /// <summary>
        /// Getter for an instance of
        /// <see cref="ProductData"/>
        /// related to iText core module.
        /// </summary>
        /// <returns>iText core product description</returns>
        public static ProductData GetInstance() {
            return ITEXT_PRODUCT_DATA;
        }
    }
}