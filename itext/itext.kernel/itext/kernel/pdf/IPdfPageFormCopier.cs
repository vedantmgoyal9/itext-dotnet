/*
This file is part of the iText (R) project.
Copyright (c) 1998-2023 Apryse Group NV
Authors: Apryse Software.

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
namespace iText.Kernel.Pdf {
    /// <summary>
    /// This interface extends the logic of the {#link IPdfPageExtraCopier} interface to
    /// copy AcroForm fields to a new page.
    /// </summary>
    public interface IPdfPageFormCopier : IPdfPageExtraCopier {
        /// <summary>Create Acroform from its PDF object to process form field objects added to the Acroform during copying.
        ///     </summary>
        /// <remarks>
        /// Create Acroform from its PDF object to process form field objects added to the Acroform during copying.
        /// <para />
        /// All pages must already be copied to the target document before calling this. So fields with the same names will
        /// be merged and target document tag structure will be correct.
        /// </remarks>
        /// <param name="documentTo">the target document.</param>
        void RecreateAcroformToProcessCopiedFields(PdfDocument documentTo);
    }
}