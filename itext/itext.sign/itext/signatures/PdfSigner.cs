/*
This file is part of the iText (R) project.
Copyright (c) 1998-2024 Apryse Group NV
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
using System;
using System.Collections.Generic;
using System.IO;
using iText.Bouncycastleconnector;
using iText.Commons.Bouncycastle;
using iText.Commons.Bouncycastle.Asn1;
using iText.Commons.Bouncycastle.Asn1.Esf;
using iText.Commons.Bouncycastle.Cert;
using iText.Commons.Bouncycastle.Crypto;
using iText.Commons.Digest;
using iText.Commons.Utils;
using iText.Forms;
using iText.Forms.Fields;
using iText.Forms.Fields.Properties;
using iText.Forms.Form.Element;
using iText.Forms.Util;
using iText.IO.Source;
using iText.IO.Util;
using iText.Kernel.Crypto;
using iText.Kernel.Exceptions;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Mac;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Tagutils;
using iText.Kernel.Utils;
using iText.Kernel.Validation;
using iText.Kernel.Validation.Context;
using iText.Layout.Properties;
using iText.Layout.Tagging;
using iText.Pdfa;
using iText.Pdfa.Checker;
using iText.Signatures.Cms;
using iText.Signatures.Exceptions;
using iText.Signatures.Mac;

namespace iText.Signatures {
    /// <summary>Takes care of the cryptographic options and appearances that form a signature.</summary>
    public class PdfSigner {
//\cond DO_NOT_DOCUMENT
        internal const int MAXIMUM_MAC_SIZE = 788;
//\endcond

        private static readonly IBouncyCastleFactory FACTORY = BouncyCastleFactoryCreator.GetFactory();

        private const String ID_ATTR_PDF_MAC_DATA = "1.0.32004.1.2";

        /// <summary>Enum containing the Cryptographic Standards.</summary>
        /// <remarks>Enum containing the Cryptographic Standards. Possible values are "CMS" and "CADES".</remarks>
        public enum CryptoStandard {
            /// <summary>Cryptographic Message Syntax.</summary>
            CMS,
            /// <summary>CMS Advanced Electronic Signatures.</summary>
            CADES
        }

        /// <summary>The file right before the signature is added (can be null).</summary>
        protected internal FileStream raf;

        /// <summary>The bytes of the file right before the signature is added (if raf is null).</summary>
        protected internal byte[] bout;

        /// <summary>Array containing the byte positions of the bytes that need to be hashed.</summary>
        protected internal long[] range;

        /// <summary>The PdfDocument.</summary>
        protected internal PdfDocument document;

        /// <summary>The crypto dictionary.</summary>
        protected internal PdfSignature cryptoDictionary;

        /// <summary>Holds value of property signatureEvent.</summary>
        protected internal PdfSigner.ISignatureEvent signatureEvent;

        /// <summary>OutputStream for the bytes of the document.</summary>
        protected internal Stream originalOS;

        /// <summary>Outputstream that temporarily holds the output in memory.</summary>
        protected internal MemoryStream temporaryOS;

        /// <summary>Tempfile to hold the output temporarily.</summary>
        protected internal FileInfo tempFile;

        /// <summary>Name and content of keys that can only be added in the close() method.</summary>
        protected internal IDictionary<PdfName, PdfLiteral> exclusionLocations;

        /// <summary>Indicates if the pdf document has already been pre-closed.</summary>
        protected internal bool preClosed = false;

        /// <summary>Boolean to check if this PdfSigner instance has been closed already or not.</summary>
        protected internal bool closed = false;

        /// <summary>AcroForm for the PdfDocument.</summary>
        private readonly PdfAcroForm acroForm;

        /// <summary>The name of the signer extracted from the signing certificate.</summary>
        private String signerName = "";

        /// <summary>Properties to be used in signing operations.</summary>
        private SignerProperties signerProperties = new SignerProperties();

        /// <summary>Creates a PdfSigner instance.</summary>
        /// <remarks>
        /// Creates a PdfSigner instance. Uses a
        /// <see cref="System.IO.MemoryStream"/>
        /// instead of a temporary file.
        /// </remarks>
        /// <param name="reader">PdfReader that reads the PDF file</param>
        /// <param name="outputStream">OutputStream to write the signed PDF file</param>
        /// <param name="properties">
        /// 
        /// <see cref="iText.Kernel.Pdf.StampingProperties"/>
        /// for the signing document. Note that encryption will be
        /// preserved regardless of what is set in properties.
        /// </param>
        public PdfSigner(PdfReader reader, Stream outputStream, StampingProperties properties)
            : this(reader, outputStream, null, properties) {
        }

        /// <summary>Creates a PdfSigner instance.</summary>
        /// <remarks>
        /// Creates a PdfSigner instance. Uses a
        /// <see cref="System.IO.MemoryStream"/>
        /// instead of a temporary file.
        /// </remarks>
        /// <param name="reader">PdfReader that reads the PDF file</param>
        /// <param name="outputStream">OutputStream to write the signed PDF file</param>
        /// <param name="path">File to which the output is temporarily written</param>
        /// <param name="stampingProperties">
        /// 
        /// <see cref="iText.Kernel.Pdf.StampingProperties"/>
        /// for the signing document. Note that encryption will be
        /// preserved regardless of what is set in properties.
        /// </param>
        /// <param name="signerProperties">
        /// 
        /// <see cref="SignerProperties"/>
        /// bundled properties to be used in signing operations.
        /// </param>
        public PdfSigner(PdfReader reader, Stream outputStream, String path, StampingProperties stampingProperties
            , SignerProperties signerProperties)
            : this(reader, outputStream, path, stampingProperties) {
            this.signerProperties = signerProperties;
            UpdateFieldName();
        }

        /// <summary>Creates a PdfSigner instance.</summary>
        /// <remarks>
        /// Creates a PdfSigner instance. Uses a
        /// <see cref="System.IO.MemoryStream"/>
        /// instead of a temporary file.
        /// </remarks>
        /// <param name="reader">PdfReader that reads the PDF file</param>
        /// <param name="outputStream">OutputStream to write the signed PDF file</param>
        /// <param name="path">File to which the output is temporarily written</param>
        /// <param name="properties">
        /// 
        /// <see cref="iText.Kernel.Pdf.StampingProperties"/>
        /// for the signing document. Note that encryption will be
        /// preserved regardless of what is set in properties.
        /// </param>
        public PdfSigner(PdfReader reader, Stream outputStream, String path, StampingProperties properties) {
            StampingProperties localProps = new StampingProperties(properties).PreserveEncryption();
            localProps.RegisterDependency(typeof(IMacContainerLocator), new SignatureMacContainerLocator());
            if (path == null) {
                this.temporaryOS = new MemoryStream();
                this.document = InitDocument(reader, new PdfWriter(temporaryOS), localProps);
            }
            else {
                this.tempFile = FileUtil.CreateTempFile(path);
                this.document = InitDocument(reader, new PdfWriter(FileUtil.GetFileOutputStream(tempFile)), localProps);
            }
            this.acroForm = PdfFormCreator.GetAcroForm(document, true);
            this.originalOS = outputStream;
            this.signerProperties.SetFieldName(GetNewSigFieldName());
        }

//\cond DO_NOT_DOCUMENT
        internal PdfSigner(PdfDocument document, Stream outputStream, MemoryStream temporaryOS, FileInfo tempFile) {
            if (tempFile == null) {
                this.temporaryOS = temporaryOS;
            }
            else {
                this.tempFile = tempFile;
            }
            this.document = document;
            this.acroForm = PdfFormCreator.GetAcroForm(document, true);
            this.originalOS = outputStream;
            this.signerProperties.SetFieldName(GetNewSigFieldName());
        }
//\endcond

        /// <summary>
        /// Initialize new
        /// <see cref="iText.Kernel.Pdf.PdfDocument"/>
        /// instance by using provided parameters.
        /// </summary>
        /// <param name="reader">
        /// 
        /// <see cref="iText.Kernel.Pdf.PdfReader"/>
        /// to be used as a reader in the new document
        /// </param>
        /// <param name="writer">
        /// 
        /// <see cref="iText.Kernel.Pdf.PdfWriter"/>
        /// to be used as a writer in the new document
        /// </param>
        /// <param name="properties">
        /// 
        /// <see cref="iText.Kernel.Pdf.StampingProperties"/>
        /// to be provided in the new document
        /// </param>
        /// <returns>
        /// new
        /// <see cref="iText.Kernel.Pdf.PdfDocument"/>
        /// instance
        /// </returns>
        protected internal virtual PdfDocument InitDocument(PdfReader reader, PdfWriter writer, StampingProperties
             properties) {
            // TODO DEVSIX-8676 Enable keeping A and UA conformance in PdfSigner
            // TODO DEVSIX-8677 let users preserve document's conformance without knowing upfront their conformance
            return new PdfSigner.PdfSignerDocument(reader, writer, properties);
        }

        /// <summary>Sets the properties to be used in signing operations.</summary>
        /// <param name="properties">the signer properties</param>
        /// <returns>this instance to support fluent interface</returns>
        public virtual PdfSigner SetSignerProperties(SignerProperties properties) {
            this.signerProperties = properties;
            UpdateFieldName();
            return this;
        }

        /// <summary>Gets the properties to be used in signing operations.</summary>
        /// <returns>the signer properties</returns>
        public virtual SignerProperties GetSignerProperties() {
            return this.signerProperties;
        }

        /// <summary>Returns the user made signature dictionary.</summary>
        /// <remarks>
        /// Returns the user made signature dictionary. This is the dictionary at the /V key
        /// of the signature field.
        /// </remarks>
        /// <returns>the user made signature dictionary</returns>
        public virtual PdfSignature GetSignatureDictionary() {
            return cryptoDictionary;
        }

        /// <summary>Getter for property signatureEvent.</summary>
        /// <returns>value of property signatureEvent</returns>
        public virtual PdfSigner.ISignatureEvent GetSignatureEvent() {
            return this.signatureEvent;
        }

        /// <summary>Sets the signature event to allow modification of the signature dictionary.</summary>
        /// <param name="signatureEvent">the signature event</param>
        public virtual void SetSignatureEvent(PdfSigner.ISignatureEvent signatureEvent) {
            this.signatureEvent = signatureEvent;
        }

        /// <summary>Gets a new signature field name that doesn't clash with any existing name.</summary>
        /// <returns>A new signature field name.</returns>
        public virtual String GetNewSigFieldName() {
            String name = "Signature";
            int step = 1;
            while (acroForm.GetField(name + step) != null) {
                ++step;
            }
            return name + step;
        }

        /// <summary>Gets the PdfDocument associated with this instance.</summary>
        /// <returns>the PdfDocument associated with this instance</returns>
        public virtual PdfDocument GetDocument() {
            return document;
        }

        /// <summary>Sets the PdfDocument.</summary>
        /// <param name="document">The PdfDocument</param>
        protected internal virtual void SetDocument(PdfDocument document) {
            if (null == document.GetReader()) {
                throw new ArgumentException(SignExceptionMessageConstant.DOCUMENT_MUST_HAVE_READER);
            }
            this.document = document;
        }

        /// <summary>Setter for the OutputStream.</summary>
        /// <param name="originalOS">OutputStream for the bytes of the document</param>
        public virtual void SetOriginalOutputStream(Stream originalOS) {
            this.originalOS = originalOS;
        }

        /// <summary>Gets the signature field to be signed.</summary>
        /// <remarks>
        /// Gets the signature field to be signed. The field can already be presented in the document. If the field is
        /// not presented in the document, it will be created.
        /// <para />
        /// This field instance is expected to be used for setting appearance related properties such as
        /// <see cref="iText.Forms.Fields.PdfSignatureFormField.SetReuseAppearance(bool)"/>
        /// ,
        /// <see cref="iText.Forms.Fields.PdfSignatureFormField.SetBackgroundLayer(iText.Kernel.Pdf.Xobject.PdfFormXObject)
        ///     "/>
        /// and
        /// <see cref="iText.Forms.Fields.PdfSignatureFormField.SetSignatureAppearanceLayer(iText.Kernel.Pdf.Xobject.PdfFormXObject)
        ///     "/>.
        /// <para />
        /// Note that for the new signature field
        /// <see cref="SignerProperties.SetPageRect(iText.Kernel.Geom.Rectangle)"/>
        /// and
        /// <see cref="SignerProperties.SetPageNumber(int)"/>
        /// should be called before this method.
        /// </remarks>
        /// <returns>
        /// the
        /// <see cref="iText.Forms.Fields.PdfSignatureFormField"/>
        /// instance
        /// </returns>
        public virtual PdfSignatureFormField GetSignatureField() {
            PdfFormField field = acroForm.GetField(GetFieldName());
            if (field == null) {
                PdfSignatureFormField sigField = new SignatureFormFieldBuilder(document, this.signerProperties.GetFieldName
                    ()).SetWidgetRectangle(this.signerProperties.GetPageRect()).SetPage(this.signerProperties.GetPageNumber
                    ()).CreateSignature();
                acroForm.AddField(sigField);
                if (acroForm.GetPdfObject().IsIndirect()) {
                    acroForm.SetModified();
                }
                else {
                    // Acroform dictionary is a Direct dictionary,
                    // for proper flushing, catalog needs to be marked as modified
                    document.GetCatalog().SetModified();
                }
                return sigField;
            }
            if (field is PdfSignatureFormField) {
                return (PdfSignatureFormField)field;
            }
            return null;
        }

        /// <summary>Signs the document using the detached mode, CMS or CAdES equivalent.</summary>
        /// <remarks>
        /// Signs the document using the detached mode, CMS or CAdES equivalent.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="externalSignature">the interface providing the actual signing</param>
        /// <param name="chain">the certificate chain</param>
        /// <param name="crlList">the CRL list</param>
        /// <param name="ocspClient">the OCSP client</param>
        /// <param name="tsaClient">the Timestamp client</param>
        /// <param name="externalDigest">an implementation that provides the digest</param>
        /// <param name="estimatedSize">the reserved size for the signature. It will be estimated if 0</param>
        /// <param name="sigtype">Either Signature.CMS or Signature.CADES</param>
        public virtual void SignDetached(IExternalDigest externalDigest, IExternalSignature externalSignature, IX509Certificate
            [] chain, ICollection<ICrlClient> crlList, IOcspClient ocspClient, ITSAClient tsaClient, int estimatedSize
            , PdfSigner.CryptoStandard sigtype) {
            SignDetached(externalDigest, externalSignature, chain, crlList, ocspClient, tsaClient, estimatedSize, sigtype
                , (ISignaturePolicyIdentifier)null);
        }

        /// <summary>Signs the document using the detached mode, CMS or CAdES equivalent.</summary>
        /// <remarks>
        /// Signs the document using the detached mode, CMS or CAdES equivalent.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="externalSignature">the interface providing the actual signing</param>
        /// <param name="chain">the certificate chain</param>
        /// <param name="crlList">the CRL list</param>
        /// <param name="ocspClient">the OCSP client</param>
        /// <param name="tsaClient">the Timestamp client</param>
        /// <param name="estimatedSize">the reserved size for the signature. It will be estimated if 0</param>
        /// <param name="sigtype">Either Signature.CMS or Signature.CADES</param>
        public virtual void SignDetached(IExternalSignature externalSignature, IX509Certificate[] chain, ICollection
            <ICrlClient> crlList, IOcspClient ocspClient, ITSAClient tsaClient, int estimatedSize, PdfSigner.CryptoStandard
             sigtype) {
            SignDetached(new BouncyCastleDigest(), externalSignature, chain, crlList, ocspClient, tsaClient, estimatedSize
                , sigtype, (ISignaturePolicyIdentifier)null);
        }

        /// <summary>Signs the document using the detached mode, CMS or CAdES equivalent.</summary>
        /// <remarks>
        /// Signs the document using the detached mode, CMS or CAdES equivalent.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="externalSignature">the interface providing the actual signing</param>
        /// <param name="chain">the certificate chain</param>
        /// <param name="crlList">the CRL list</param>
        /// <param name="ocspClient">the OCSP client</param>
        /// <param name="tsaClient">the Timestamp client</param>
        /// <param name="externalDigest">an implementation that provides the digest</param>
        /// <param name="estimatedSize">the reserved size for the signature. It will be estimated if 0</param>
        /// <param name="sigtype">Either Signature.CMS or Signature.CADES</param>
        /// <param name="signaturePolicy">the signature policy (for EPES signatures)</param>
        public virtual void SignDetached(IExternalDigest externalDigest, IExternalSignature externalSignature, IX509Certificate
            [] chain, ICollection<ICrlClient> crlList, IOcspClient ocspClient, ITSAClient tsaClient, int estimatedSize
            , PdfSigner.CryptoStandard sigtype, SignaturePolicyInfo signaturePolicy) {
            SignDetached(externalDigest, externalSignature, chain, crlList, ocspClient, tsaClient, estimatedSize, sigtype
                , signaturePolicy.ToSignaturePolicyIdentifier());
        }

        /// <summary>Signs the document using the detached mode, CMS or CAdES equivalent.</summary>
        /// <remarks>
        /// Signs the document using the detached mode, CMS or CAdES equivalent.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="externalSignature">the interface providing the actual signing</param>
        /// <param name="chain">the certificate chain</param>
        /// <param name="crlList">the CRL list</param>
        /// <param name="ocspClient">the OCSP client</param>
        /// <param name="tsaClient">the Timestamp client</param>
        /// <param name="estimatedSize">the reserved size for the signature. It will be estimated if 0</param>
        /// <param name="sigtype">Either Signature.CMS or Signature.CADES</param>
        /// <param name="signaturePolicy">the signature policy (for EPES signatures)</param>
        public virtual void SignDetached(IExternalSignature externalSignature, IX509Certificate[] chain, ICollection
            <ICrlClient> crlList, IOcspClient ocspClient, ITSAClient tsaClient, int estimatedSize, PdfSigner.CryptoStandard
             sigtype, SignaturePolicyInfo signaturePolicy) {
            SignDetached(new BouncyCastleDigest(), externalSignature, chain, crlList, ocspClient, tsaClient, estimatedSize
                , sigtype, signaturePolicy);
        }

        /// <summary>Signs the document using the detached mode, CMS or CAdES equivalent.</summary>
        /// <remarks>
        /// Signs the document using the detached mode, CMS or CAdES equivalent.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="externalSignature">the interface providing the actual signing</param>
        /// <param name="chain">the certificate chain</param>
        /// <param name="crlList">the CRL list</param>
        /// <param name="ocspClient">the OCSP client</param>
        /// <param name="tsaClient">the Timestamp client</param>
        /// <param name="estimatedSize">the reserved size for the signature. It will be estimated if 0</param>
        /// <param name="sigtype">Either Signature.CMS or Signature.CADES</param>
        /// <param name="signaturePolicy">the signature policy (for EPES signatures)</param>
        public virtual void SignDetached(IExternalSignature externalSignature, IX509Certificate[] chain, ICollection
            <ICrlClient> crlList, IOcspClient ocspClient, ITSAClient tsaClient, int estimatedSize, PdfSigner.CryptoStandard
             sigtype, ISignaturePolicyIdentifier signaturePolicy) {
            SignDetached(new BouncyCastleDigest(), externalSignature, chain, crlList, ocspClient, tsaClient, estimatedSize
                , sigtype, signaturePolicy);
        }

        /// <summary>Signs the document using the detached mode, CMS or CAdES equivalent.</summary>
        /// <remarks>
        /// Signs the document using the detached mode, CMS or CAdES equivalent.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="externalSignature">the interface providing the actual signing</param>
        /// <param name="chain">the certificate chain</param>
        /// <param name="crlList">the CRL list</param>
        /// <param name="ocspClient">the OCSP client</param>
        /// <param name="tsaClient">the Timestamp client</param>
        /// <param name="externalDigest">an implementation that provides the digest</param>
        /// <param name="estimatedSize">the reserved size for the signature. It will be estimated if 0</param>
        /// <param name="sigtype">Either Signature.CMS or Signature.CADES</param>
        /// <param name="signaturePolicy">the signature policy (for EPES signatures)</param>
        public virtual void SignDetached(IExternalDigest externalDigest, IExternalSignature externalSignature, IX509Certificate
            [] chain, ICollection<ICrlClient> crlList, IOcspClient ocspClient, ITSAClient tsaClient, int estimatedSize
            , PdfSigner.CryptoStandard sigtype, ISignaturePolicyIdentifier signaturePolicy) {
            if (closed) {
                throw new PdfException(SignExceptionMessageConstant.THIS_INSTANCE_OF_PDF_SIGNER_ALREADY_CLOSED);
            }
            if ((int)(this.signerProperties.GetCertificationLevel()) > 0 && IsDocumentPdf2()) {
                if (DocumentContainsCertificationOrApprovalSignatures()) {
                    throw new PdfException(SignExceptionMessageConstant.CERTIFICATION_SIGNATURE_CREATION_FAILED_DOC_SHALL_NOT_CONTAIN_SIGS
                        );
                }
            }
            document.CheckIsoConformance(new SignTypeValidationContext(sigtype == PdfSigner.CryptoStandard.CADES));
            ICollection<byte[]> crlBytes = null;
            int i = 0;
            while (crlBytes == null && i < chain.Length) {
                crlBytes = ProcessCrl(chain[i++], crlList);
            }
            if (estimatedSize == 0) {
                estimatedSize = 8192;
                if (crlBytes != null) {
                    foreach (byte[] element in crlBytes) {
                        estimatedSize += element.Length + 10;
                    }
                }
                if (ocspClient != null) {
                    estimatedSize += 4192;
                }
                if (tsaClient != null) {
                    estimatedSize += tsaClient.GetTokenSizeEstimate() + 96;
                }
                if (document.GetDiContainer().GetInstance<IMacContainerLocator>().IsMacContainerLocated()) {
                    // If MAC container was located, we presume MAC will be embedded and allocate additional space.
                    estimatedSize += MAXIMUM_MAC_SIZE;
                }
            }
            this.signerName = PdfSigner.GetSignerName((IX509Certificate)chain[0]);
            if (sigtype == PdfSigner.CryptoStandard.CADES && !IsDocumentPdf2()) {
                AddDeveloperExtension(PdfDeveloperExtension.ESIC_1_7_EXTENSIONLEVEL2);
            }
            if (externalSignature.GetSignatureAlgorithmName().StartsWith("Ed")) {
                AddDeveloperExtension(PdfDeveloperExtension.ISO_32002);
            }
            // Note: at this level of abstraction, we have no easy way of determining whether we are signing using a
            // specific ECDSA curve, so we can't auto-declare the extension safely, since we don't know whether
            // the curve is on the ISO/TS 32002 allowed curves list. That responsibility is delegated to the user.
            String hashAlgorithm = externalSignature.GetDigestAlgorithmName();
            if (hashAlgorithm.StartsWith("SHA3-") || hashAlgorithm.Equals(DigestAlgorithms.SHAKE256)) {
                AddDeveloperExtension(PdfDeveloperExtension.ISO_32001);
            }
            PdfSignature dic = new PdfSignature(PdfName.Adobe_PPKLite, sigtype == PdfSigner.CryptoStandard.CADES ? PdfName
                .ETSI_CAdES_DETACHED : PdfName.Adbe_pkcs7_detached);
            dic.SetReason(this.signerProperties.GetReason());
            dic.SetLocation(this.signerProperties.GetLocation());
            dic.SetSignatureCreator(this.signerProperties.GetSignatureCreator());
            dic.SetContact(this.signerProperties.GetContact());
            DateTime claimedSignDate = this.signerProperties.GetClaimedSignDate();
            if (claimedSignDate != TimestampConstants.UNDEFINED_TIMESTAMP_DATE) {
                dic.SetDate(new PdfDate(claimedSignDate));
            }
            // time-stamp will over-rule this
            cryptoDictionary = dic;
            IDictionary<PdfName, int?> exc = new Dictionary<PdfName, int?>();
            exc.Put(PdfName.Contents, estimatedSize * 2 + 2);
            PreClose(exc);
            PdfPKCS7 sgn = new PdfPKCS7((IPrivateKey)null, chain, hashAlgorithm, externalDigest, false);
            if (signaturePolicy != null) {
                sgn.SetSignaturePolicy(signaturePolicy);
            }
            Stream data = GetRangeStream();
            byte[] hash = DigestAlgorithms.Digest(data, SignUtils.GetMessageDigest(hashAlgorithm, externalDigest));
            IList<byte[]> ocspList = new List<byte[]>();
            if (chain.Length > 1 && ocspClient != null) {
                for (int j = 0; j < chain.Length - 1; ++j) {
                    byte[] ocsp = ocspClient.GetEncoded((IX509Certificate)chain[j], (IX509Certificate)chain[j + 1], null);
                    if (ocsp != null && BouncyCastleFactoryCreator.GetFactory().CreateCertificateStatus().GetGood().Equals(OcspClientBouncyCastle
                        .GetCertificateStatus(ocsp))) {
                        ocspList.Add(ocsp);
                    }
                }
            }
            byte[] sh = sgn.GetAuthenticatedAttributeBytes(hash, sigtype, ocspList, crlBytes);
            byte[] extSignature = externalSignature.Sign(sh);
            sgn.SetExternalSignatureValue(extSignature, null, externalSignature.GetSignatureAlgorithmName(), externalSignature
                .GetSignatureMechanismParameters());
            document.DispatchEvent(new SignatureContainerGenerationEvent(sgn.GetUnsignedAttributes(), extSignature, GetRangeStream
                ()));
            byte[] encodedSig = sgn.GetEncodedPKCS7(hash, sigtype, tsaClient, ocspList, crlBytes);
            if (estimatedSize < encodedSig.Length) {
                throw new System.IO.IOException("Not enough space");
            }
            byte[] paddedSig = new byte[estimatedSize];
            Array.Copy(encodedSig, 0, paddedSig, 0, encodedSig.Length);
            PdfDictionary dic2 = new PdfDictionary();
            dic2.Put(PdfName.Contents, new PdfString(paddedSig).SetHexWriting(true));
            Close(dic2);
            closed = true;
        }

        /// <summary>Sign the document using an external container, usually a PKCS7.</summary>
        /// <remarks>
        /// Sign the document using an external container, usually a PKCS7. The signature is fully composed
        /// externally, iText will just put the container inside the document.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="externalSignatureContainer">the interface providing the actual signing</param>
        /// <param name="estimatedSize">the reserved size for the signature</param>
        public virtual void SignExternalContainer(IExternalSignatureContainer externalSignatureContainer, int estimatedSize
            ) {
            if (closed) {
                throw new PdfException(SignExceptionMessageConstant.THIS_INSTANCE_OF_PDF_SIGNER_ALREADY_CLOSED);
            }
            PdfSignature dic = CreateSignatureDictionary(true);
            externalSignatureContainer.ModifySigningDictionary(dic.GetPdfObject());
            cryptoDictionary = dic;
            if (document.GetDiContainer().GetInstance<IMacContainerLocator>().IsMacContainerLocated()) {
                // If MAC container was located, we presume MAC will be embedded and allocate additional space.
                estimatedSize += MAXIMUM_MAC_SIZE;
            }
            IDictionary<PdfName, int?> exc = new Dictionary<PdfName, int?>();
            exc.Put(PdfName.Contents, estimatedSize * 2 + 2);
            PreClose(exc);
            Stream data = GetRangeStream();
            byte[] encodedSig = externalSignatureContainer.Sign(data);
            if (document.GetDiContainer().GetInstance<IMacContainerLocator>().IsMacContainerLocated()) {
                encodedSig = EmbedMacTokenIntoSignatureContainer(encodedSig);
            }
            if (estimatedSize < encodedSig.Length) {
                throw new System.IO.IOException(SignExceptionMessageConstant.NOT_ENOUGH_SPACE);
            }
            byte[] paddedSig = new byte[estimatedSize];
            Array.Copy(encodedSig, 0, paddedSig, 0, encodedSig.Length);
            PdfDictionary dic2 = new PdfDictionary();
            dic2.Put(PdfName.Contents, new PdfString(paddedSig).SetHexWriting(true));
            Close(dic2);
            closed = true;
        }

        /// <summary>Signs a document with a PAdES-LTV Timestamp.</summary>
        /// <remarks>
        /// Signs a document with a PAdES-LTV Timestamp. The document is closed at the end.
        /// <br /><br />
        /// NOTE: This method closes the underlying pdf document. This means, that current instance
        /// of PdfSigner cannot be used after this method call.
        /// </remarks>
        /// <param name="tsa">the timestamp generator</param>
        /// <param name="signatureName">
        /// the signature name or null to have a name generated
        /// automatically
        /// </param>
        public virtual void Timestamp(ITSAClient tsa, String signatureName) {
            if (closed) {
                throw new PdfException(SignExceptionMessageConstant.THIS_INSTANCE_OF_PDF_SIGNER_ALREADY_CLOSED);
            }
            if (tsa == null) {
                throw new PdfException(SignExceptionMessageConstant.PROVIDED_TSA_CLIENT_IS_NULL);
            }
            int contentEstimated = tsa.GetTokenSizeEstimate();
            if (document.GetDiContainer().GetInstance<IMacContainerLocator>().IsMacContainerLocated()) {
                // If MAC container was located, we presume MAC will be embedded and allocate additional space.
                contentEstimated += MAXIMUM_MAC_SIZE;
            }
            if (!IsDocumentPdf2()) {
                AddDeveloperExtension(PdfDeveloperExtension.ESIC_1_7_EXTENSIONLEVEL5);
            }
            this.signerProperties.SetFieldName(signatureName);
            PdfSignature dic = new PdfSignature(PdfName.Adobe_PPKLite, PdfName.ETSI_RFC3161);
            dic.Put(PdfName.Type, PdfName.DocTimeStamp);
            cryptoDictionary = dic;
            IDictionary<PdfName, int?> exc = new Dictionary<PdfName, int?>();
            exc.Put(PdfName.Contents, contentEstimated * 2 + 2);
            PreClose(exc);
            Stream data = GetRangeStream();
            IMessageDigest messageDigest = tsa.GetMessageDigest();
            byte[] buf = new byte[4096];
            int n;
            while ((n = data.Read(buf)) > 0) {
                messageDigest.Update(buf, 0, n);
            }
            byte[] tsImprint = messageDigest.Digest();
            byte[] tsToken;
            try {
                tsToken = tsa.GetTimeStampToken(tsImprint);
            }
            catch (Exception e) {
                throw iText.Bouncycastleconnector.BouncyCastleFactoryCreator.GetFactory().CreateGeneralSecurityException(e
                    .Message, e);
            }
            if (document.GetDiContainer().GetInstance<IMacContainerLocator>().IsMacContainerLocated()) {
                tsToken = EmbedMacTokenIntoSignatureContainer(tsToken);
            }
            if (contentEstimated + 2 < tsToken.Length) {
                throw new System.IO.IOException(MessageFormatUtil.Format(SignExceptionMessageConstant.TOKEN_ESTIMATION_SIZE_IS_NOT_LARGE_ENOUGH
                    , contentEstimated, tsToken.Length));
            }
            byte[] paddedSig = new byte[contentEstimated];
            Array.Copy(tsToken, 0, paddedSig, 0, tsToken.Length);
            PdfDictionary dic2 = new PdfDictionary();
            dic2.Put(PdfName.Contents, new PdfString(paddedSig).SetHexWriting(true));
            Close(dic2);
            closed = true;
        }

        /// <summary>Signs a PDF where space was already reserved.</summary>
        /// <param name="document">the original PDF</param>
        /// <param name="fieldName">the field to sign. It must be the last field</param>
        /// <param name="outs">the output PDF</param>
        /// <param name="externalSignatureContainer">
        /// the signature container doing the actual signing. Only the
        /// method ExternalSignatureContainer.sign is used
        /// </param>
        [System.ObsoleteAttribute(@"SignDeferred(iText.Kernel.Pdf.PdfDocument, System.String, System.IO.Stream, IExternalSignatureContainer) should be used instead."
            )]
        public static void SignDeferred(PdfDocument document, String fieldName, Stream outs, IExternalSignatureContainer
             externalSignatureContainer) {
            PdfSigner.SignatureApplier applier = new PdfSigner.SignatureApplier(document, fieldName, outs);
            applier.Apply((a) => externalSignatureContainer.Sign(a.GetDataToSign()));
        }

        /// <summary>Signs a PDF where space was already reserved.</summary>
        /// <param name="reader">
        /// 
        /// <see cref="iText.Kernel.Pdf.PdfReader"/>
        /// that reads the PDF file
        /// </param>
        /// <param name="fieldName">the field to sign. It must be the last field</param>
        /// <param name="outs">the output PDF</param>
        /// <param name="externalSignatureContainer">
        /// the signature container doing the actual signing. Only the
        /// method ExternalSignatureContainer.sign is used
        /// </param>
        public static void SignDeferred(PdfReader reader, String fieldName, Stream outs, IExternalSignatureContainer
             externalSignatureContainer) {
            PdfSigner.SignatureApplier applier = new PdfSigner.SignatureApplier(reader, fieldName, outs);
            applier.Apply((a) => externalSignatureContainer.Sign(a.GetDataToSign()));
        }

        /// <summary>Processes a CRL list.</summary>
        /// <param name="cert">a Certificate if one of the CrlList implementations needs to retrieve the CRL URL from it.
        ///     </param>
        /// <param name="crlList">a list of CrlClient implementations</param>
        /// <returns>a collection of CRL bytes that can be embedded in a PDF</returns>
        protected internal virtual ICollection<byte[]> ProcessCrl(IX509Certificate cert, ICollection<ICrlClient> crlList
            ) {
            if (crlList == null) {
                return null;
            }
            IList<byte[]> crlBytes = new List<byte[]>();
            foreach (ICrlClient cc in crlList) {
                if (cc == null) {
                    continue;
                }
                ICollection<byte[]> b = cc.GetEncoded((IX509Certificate)cert, null);
                if (b == null) {
                    continue;
                }
                crlBytes.AddAll(b);
            }
            return crlBytes.IsEmpty() ? null : crlBytes;
        }

        /// <summary>
        /// Add developer extension to the current
        /// <see cref="iText.Kernel.Pdf.PdfDocument"/>.
        /// </summary>
        /// <param name="extension">
        /// 
        /// <see cref="iText.Kernel.Pdf.PdfDeveloperExtension"/>
        /// to be added
        /// </param>
        protected internal virtual void AddDeveloperExtension(PdfDeveloperExtension extension) {
            document.GetCatalog().AddDeveloperExtension(extension);
        }

        /// <summary>Checks if the document is in the process of closing.</summary>
        /// <returns>true if the document is in the process of closing, false otherwise</returns>
        protected internal virtual bool IsPreClosed() {
            return preClosed;
        }

        /// <summary>This is the first method to be called when using external signatures.</summary>
        /// <remarks>
        /// This is the first method to be called when using external signatures. The general sequence is:
        /// preClose(), getDocumentBytes() and close().
        /// <para />
        /// <c>exclusionSizes</c> must contain at least
        /// the <c>PdfName.CONTENTS</c> key with the size that it will take in the
        /// document. Note that due to the hex string coding this size should be byte_size*2+2.
        /// </remarks>
        /// <param name="exclusionSizes">
        /// Map with names and sizes to be excluded in the signature
        /// calculation. The key is a PdfName and the value an Integer.
        /// At least the /Contents must be present
        /// </param>
        protected internal virtual void PreClose(IDictionary<PdfName, int?> exclusionSizes) {
            if (preClosed) {
                throw new PdfException(SignExceptionMessageConstant.DOCUMENT_ALREADY_PRE_CLOSED);
            }
            preClosed = true;
            SignatureUtil sgnUtil = new SignatureUtil(document);
            String name = GetFieldName();
            bool fieldExist = sgnUtil.DoesSignatureFieldExist(name);
            acroForm.SetSignatureFlags(PdfAcroForm.SIGNATURE_EXIST | PdfAcroForm.APPEND_ONLY);
            PdfSigFieldLock fieldLock = null;
            if (cryptoDictionary == null) {
                throw new PdfException(SignExceptionMessageConstant.NO_CRYPTO_DICTIONARY_DEFINED);
            }
            cryptoDictionary.GetPdfObject().MakeIndirect(document);
            document.DispatchEvent(new SignatureDocumentClosingEvent(cryptoDictionary.GetPdfObject().GetIndirectReference
                ()));
            if (fieldExist) {
                fieldLock = PopulateExistingSignatureFormField(acroForm);
            }
            else {
                fieldLock = CreateNewSignatureFormField(acroForm, name);
            }
            exclusionLocations = new Dictionary<PdfName, PdfLiteral>();
            PdfLiteral lit = new PdfLiteral(80);
            exclusionLocations.Put(PdfName.ByteRange, lit);
            cryptoDictionary.Put(PdfName.ByteRange, lit);
            foreach (KeyValuePair<PdfName, int?> entry in exclusionSizes) {
                PdfName key = entry.Key;
                lit = new PdfLiteral((int)entry.Value);
                exclusionLocations.Put(key, lit);
                cryptoDictionary.Put(key, lit);
            }
            if ((int)(this.signerProperties.GetCertificationLevel()) > 0) {
                AddDocMDP(cryptoDictionary);
            }
            if (fieldLock != null) {
                AddFieldMDP(cryptoDictionary, fieldLock);
            }
            if (signatureEvent != null) {
                signatureEvent.GetSignatureDictionary(cryptoDictionary);
            }
            if ((int)(this.signerProperties.GetCertificationLevel()) > 0) {
                // add DocMDP entry to root
                PdfDictionary docmdp = new PdfDictionary();
                docmdp.Put(PdfName.DocMDP, cryptoDictionary.GetPdfObject());
                document.GetCatalog().Put(PdfName.Perms, docmdp);
                document.GetCatalog().SetModified();
            }
            document.CheckIsoConformance(new SignatureValidationContext(cryptoDictionary.GetPdfObject()));
            cryptoDictionary.GetPdfObject().Flush(false);
            document.Close();
            range = new long[exclusionLocations.Count * 2];
            long byteRangePosition = exclusionLocations.Get(PdfName.ByteRange).GetPosition();
            exclusionLocations.JRemove(PdfName.ByteRange);
            int idx = 1;
            foreach (PdfLiteral lit1 in exclusionLocations.Values) {
                long n = lit1.GetPosition();
                range[idx++] = n;
                range[idx++] = lit1.GetBytesCount() + n;
            }
            JavaUtil.Sort(range, 1, range.Length - 1);
            for (int k = 3; k < range.Length - 2; k += 2) {
                range[k] -= range[k - 1];
            }
            if (tempFile == null) {
                bout = temporaryOS.ToArray();
                range[range.Length - 1] = bout.Length - range[range.Length - 2];
                MemoryStream bos = new MemoryStream();
                PdfOutputStream os = new PdfOutputStream(bos);
                os.Write('[');
                foreach (long l in range) {
                    os.WriteLong(l).Write(' ');
                }
                os.Write(']');
                Array.Copy(bos.ToArray(), 0, bout, (int)byteRangePosition, (int)bos.Length);
            }
            else {
                try {
                    raf = FileUtil.GetRandomAccessFile(tempFile);
                    long len = raf.Length;
                    range[range.Length - 1] = len - range[range.Length - 2];
                    MemoryStream bos = new MemoryStream();
                    PdfOutputStream os = new PdfOutputStream(bos);
                    os.Write('[');
                    foreach (long l in range) {
                        os.WriteLong(l).Write(' ');
                    }
                    os.Write(']');
                    raf.Seek(byteRangePosition);
                    raf.Write(bos.ToArray(), 0, (int)bos.Length);
                }
                catch (System.IO.IOException e) {
                    try {
                        raf.Dispose();
                    }
                    catch (Exception) {
                    }
                    try {
                        tempFile.Delete();
                    }
                    catch (Exception) {
                    }
                    throw;
                }
            }
        }

        /// <summary>
        /// Returns final signature appearance object set by
        /// <see cref="SignerProperties.SetSignatureAppearance(iText.Forms.Form.Element.SignatureFieldAppearance)"/>
        /// and
        /// customized using
        /// <see cref="PdfSigner"/>
        /// properties such as signing date, reason, location and signer name
        /// in case they weren't specified by the user, or, if none was set, returns a new one with default appearance.
        /// </summary>
        /// <remarks>
        /// Returns final signature appearance object set by
        /// <see cref="SignerProperties.SetSignatureAppearance(iText.Forms.Form.Element.SignatureFieldAppearance)"/>
        /// and
        /// customized using
        /// <see cref="PdfSigner"/>
        /// properties such as signing date, reason, location and signer name
        /// in case they weren't specified by the user, or, if none was set, returns a new one with default appearance.
        /// <para />
        /// To customize the appearance of the signature, create new
        /// <see cref="iText.Forms.Form.Element.SignatureFieldAppearance"/>
        /// object and set it
        /// using
        /// <see cref="SignerProperties.SetSignatureAppearance(iText.Forms.Form.Element.SignatureFieldAppearance)"/>.
        /// <para />
        /// Note that in case you create new signature field (either use
        /// <see cref="SignerProperties.SetFieldName(System.String)"/>
        /// with the name
        /// that doesn't exist in the document or don't specify it at all) then the signature is invisible by default.
        /// <para />
        /// It is possible to set other appearance related properties such as
        /// <see cref="iText.Forms.Fields.PdfSignatureFormField.SetReuseAppearance(bool)"/>
        /// ,
        /// <see cref="iText.Forms.Fields.PdfSignatureFormField.SetBackgroundLayer(iText.Kernel.Pdf.Xobject.PdfFormXObject)
        ///     "/>
        /// (n0 layer) and
        /// <see cref="iText.Forms.Fields.PdfSignatureFormField.SetSignatureAppearanceLayer(iText.Kernel.Pdf.Xobject.PdfFormXObject)
        ///     "/>
        /// (n2 layer) for the signature field using
        /// <see cref="GetSignatureField()"/>
        /// . Page, rectangle and other properties could be set up via
        /// <see cref="SignerProperties"/>.
        /// </remarks>
        /// <returns>
        /// 
        /// <see cref="iText.Forms.Form.Element.SignatureFieldAppearance"/>
        /// object representing signature appearance
        /// </returns>
        protected internal virtual SignatureFieldAppearance GetSignatureAppearance() {
            if (this.signerProperties.GetSignatureAppearance() == null) {
                this.signerProperties.SetSignatureAppearance(new SignatureFieldAppearance(SignerProperties.IGNORED_ID));
                SetContent();
            }
            else {
                PopulateExistingModelElement();
            }
            return this.signerProperties.GetSignatureAppearance();
        }

        /// <summary>Populates already existing signature form field in the acroForm object.</summary>
        /// <remarks>
        /// Populates already existing signature form field in the acroForm object.
        /// This method is called during the
        /// <see cref="PreClose(System.Collections.Generic.IDictionary{K, V})"/>
        /// method if the signature field already exists.
        /// </remarks>
        /// <param name="acroForm">
        /// 
        /// <see cref="iText.Forms.PdfAcroForm"/>
        /// object in which the signature field will be populated
        /// </param>
        /// <returns>signature field lock dictionary</returns>
        protected internal virtual PdfSigFieldLock PopulateExistingSignatureFormField(PdfAcroForm acroForm) {
            PdfSignatureFormField sigField = (PdfSignatureFormField)acroForm.GetField(this.signerProperties.GetFieldName
                ());
            PdfSigFieldLock sigFieldLock = sigField.GetSigFieldLockDictionary();
            if (sigFieldLock == null && this.signerProperties.GetFieldLockDict() != null) {
                this.signerProperties.GetFieldLockDict().GetPdfObject().MakeIndirect(document);
                sigField.Put(PdfName.Lock, this.signerProperties.GetFieldLockDict().GetPdfObject());
                sigFieldLock = this.signerProperties.GetFieldLockDict();
            }
            sigField.Put(PdfName.P, document.GetPage(this.signerProperties.GetPageNumber()).GetPdfObject());
            sigField.Put(PdfName.V, cryptoDictionary.GetPdfObject());
            PdfObject obj = sigField.GetPdfObject().Get(PdfName.F);
            int flags = 0;
            if (obj != null && obj.IsNumber()) {
                flags = ((PdfNumber)obj).IntValue();
            }
            flags |= PdfAnnotation.LOCKED;
            sigField.Put(PdfName.F, new PdfNumber(flags));
            sigField.GetFirstFormAnnotation().SetFormFieldElement(GetSignatureAppearance());
            sigField.RegenerateField();
            sigField.SetModified();
            return sigFieldLock;
        }

        /// <summary>Creates new signature form field and adds it to the acroForm object.</summary>
        /// <remarks>
        /// Creates new signature form field and adds it to the acroForm object.
        /// This method is called during the
        /// <see cref="PreClose(System.Collections.Generic.IDictionary{K, V})"/>
        /// method if the signature field doesn't exist.
        /// </remarks>
        /// <param name="acroForm">
        /// 
        /// <see cref="iText.Forms.PdfAcroForm"/>
        /// object in which new signature field will be added
        /// </param>
        /// <param name="name">the name of the field</param>
        /// <returns>signature field lock dictionary</returns>
        protected internal virtual PdfSigFieldLock CreateNewSignatureFormField(PdfAcroForm acroForm, String name) {
            PdfWidgetAnnotation widget = new PdfWidgetAnnotation(this.signerProperties.GetPageRect());
            widget.SetFlags(PdfAnnotation.PRINT | PdfAnnotation.LOCKED);
            PdfSignatureFormField sigField = new SignatureFormFieldBuilder(document, name).CreateSignature();
            sigField.Put(PdfName.V, cryptoDictionary.GetPdfObject());
            sigField.AddKid(widget);
            PdfSigFieldLock sigFieldLock = sigField.GetSigFieldLockDictionary();
            if (this.signerProperties.GetFieldLockDict() != null) {
                this.signerProperties.GetFieldLockDict().GetPdfObject().MakeIndirect(document);
                sigField.Put(PdfName.Lock, this.signerProperties.GetFieldLockDict().GetPdfObject());
                sigFieldLock = this.signerProperties.GetFieldLockDict();
            }
            int pagen = this.signerProperties.GetPageNumber();
            widget.SetPage(document.GetPage(pagen));
            sigField.DisableFieldRegeneration();
            ApplyDefaultPropertiesForTheNewField(sigField);
            sigField.EnableFieldRegeneration();
            acroForm.AddField(sigField, document.GetPage(pagen));
            if (acroForm.GetPdfObject().IsIndirect()) {
                acroForm.SetModified();
            }
            else {
                //Acroform dictionary is a Direct dictionary,
                //for proper flushing, catalog needs to be marked as modified
                document.GetCatalog().SetModified();
            }
            return sigFieldLock;
        }

        /// <summary>Gets the document bytes that are hashable when using external signatures.</summary>
        /// <remarks>
        /// Gets the document bytes that are hashable when using external signatures.
        /// The general sequence is:
        /// <see cref="PreClose(System.Collections.Generic.IDictionary{K, V})"/>
        /// ,
        /// <see cref="GetRangeStream()"/>
        /// and
        /// <see cref="Close(iText.Kernel.Pdf.PdfDictionary)"/>.
        /// </remarks>
        /// <returns>
        /// the
        /// <see cref="System.IO.Stream"/>
        /// of bytes to be signed
        /// </returns>
        protected internal virtual Stream GetRangeStream() {
            RandomAccessSourceFactory fac = new RandomAccessSourceFactory();
            IRandomAccessSource randomAccessSource = fac.CreateRanged(GetUnderlyingSource(), range);
            return new RASInputStream(randomAccessSource);
        }

        /// <summary>This is the last method to be called when using external signatures.</summary>
        /// <remarks>
        /// This is the last method to be called when using external signatures. The general sequence is:
        /// preClose(), getDocumentBytes() and close().
        /// <para />
        /// update is a PdfDictionary that must have exactly the
        /// same keys as the ones provided in
        /// <see cref="PreClose(System.Collections.Generic.IDictionary{K, V})"/>.
        /// </remarks>
        /// <param name="update">
        /// a PdfDictionary with the key/value that will fill the holes defined
        /// in
        /// <see cref="PreClose(System.Collections.Generic.IDictionary{K, V})"/>
        /// </param>
        protected internal virtual void Close(PdfDictionary update) {
            try {
                if (!preClosed) {
                    throw new PdfException(SignExceptionMessageConstant.DOCUMENT_MUST_BE_PRE_CLOSED);
                }
                MemoryStream bous = new MemoryStream();
                PdfOutputStream os = new PdfOutputStream(bous);
                foreach (PdfName key in update.KeySet()) {
                    PdfObject obj = update.Get(key);
                    PdfLiteral lit = exclusionLocations.Get(key);
                    if (lit == null) {
                        throw new ArgumentException("The key didn't reserve space in preclose");
                    }
                    bous.JReset();
                    os.Write(obj);
                    if (bous.Length > lit.GetBytesCount()) {
                        throw new ArgumentException(SignExceptionMessageConstant.TOO_BIG_KEY);
                    }
                    if (tempFile == null) {
                        Array.Copy(bous.ToArray(), 0, bout, (int)lit.GetPosition(), (int)bous.Length);
                    }
                    else {
                        raf.Seek(lit.GetPosition());
                        raf.Write(bous.ToArray(), 0, (int)bous.Length);
                    }
                }
                if (update.Size() != exclusionLocations.Count) {
                    throw new ArgumentException("The update dictionary has less keys than required");
                }
                if (tempFile == null) {
                    originalOS.Write(bout, 0, bout.Length);
                }
                else {
                    if (originalOS != null) {
                        raf.Seek(0);
                        long length = raf.Length;
                        byte[] buf = new byte[8192];
                        while (length > 0) {
                            int r = raf.JRead(buf, 0, (int)Math.Min((long)buf.Length, length));
                            if (r < 0) {
                                throw new EndOfStreamException("unexpected eof");
                            }
                            originalOS.Write(buf, 0, r);
                            length -= r;
                        }
                    }
                }
            }
            finally {
                if (tempFile != null) {
                    raf.Dispose();
                    if (originalOS != null) {
                        tempFile.Delete();
                    }
                }
                if (originalOS != null) {
                    try {
                        originalOS.Dispose();
                    }
                    catch (Exception) {
                    }
                }
            }
        }

        /// <summary>Returns the underlying source.</summary>
        /// <returns>the underlying source</returns>
        protected internal virtual IRandomAccessSource GetUnderlyingSource() {
            RandomAccessSourceFactory fac = new RandomAccessSourceFactory();
            return raf == null ? fac.CreateSource(bout) : fac.CreateSource(raf);
        }

        /// <summary>Adds keys to the signature dictionary that define the certification level and the permissions.</summary>
        /// <remarks>
        /// Adds keys to the signature dictionary that define the certification level and the permissions.
        /// This method is only used for Certifying signatures.
        /// </remarks>
        /// <param name="crypto">the signature dictionary</param>
        protected internal virtual void AddDocMDP(PdfSignature crypto) {
            PdfDictionary reference = new PdfDictionary();
            PdfDictionary transformParams = new PdfDictionary();
            transformParams.Put(PdfName.P, new PdfNumber((int)(this.signerProperties.GetCertificationLevel())));
            transformParams.Put(PdfName.V, new PdfName("1.2"));
            transformParams.Put(PdfName.Type, PdfName.TransformParams);
            reference.Put(PdfName.TransformMethod, PdfName.DocMDP);
            reference.Put(PdfName.Type, PdfName.SigRef);
            reference.Put(PdfName.TransformParams, transformParams);
            reference.Put(PdfName.Data, document.GetTrailer().Get(PdfName.Root));
            PdfArray types = new PdfArray();
            types.Add(reference);
            crypto.Put(PdfName.Reference, types);
        }

        /// <summary>Adds keys to the signature dictionary that define the field permissions.</summary>
        /// <remarks>
        /// Adds keys to the signature dictionary that define the field permissions.
        /// This method is only used for signatures that lock fields.
        /// </remarks>
        /// <param name="crypto">the signature dictionary</param>
        /// <param name="fieldLock">
        /// the
        /// <see cref="iText.Forms.PdfSigFieldLock"/>
        /// instance specified the field lock to be set
        /// </param>
        protected internal virtual void AddFieldMDP(PdfSignature crypto, PdfSigFieldLock fieldLock) {
            PdfDictionary reference = new PdfDictionary();
            PdfDictionary transformParams = new PdfDictionary();
            transformParams.PutAll(fieldLock.GetPdfObject());
            transformParams.Put(PdfName.Type, PdfName.TransformParams);
            transformParams.Put(PdfName.V, new PdfName("1.2"));
            reference.Put(PdfName.TransformMethod, PdfName.FieldMDP);
            reference.Put(PdfName.Type, PdfName.SigRef);
            reference.Put(PdfName.TransformParams, transformParams);
            reference.Put(PdfName.Data, document.GetTrailer().Get(PdfName.Root));
            PdfArray types = crypto.GetPdfObject().GetAsArray(PdfName.Reference);
            if (types == null) {
                types = new PdfArray();
                crypto.Put(PdfName.Reference, types);
            }
            types.Add(reference);
        }

        /// <summary>Check if current document instance already contains certification or approval signatures.</summary>
        /// <returns>
        /// 
        /// <see langword="true"/>
        /// if document contains certification or approval signatures,
        /// <see langword="false"/>
        /// otherwise
        /// </returns>
        protected internal virtual bool DocumentContainsCertificationOrApprovalSignatures() {
            bool containsCertificationOrApprovalSignature = false;
            PdfDictionary urSignature = null;
            PdfDictionary catalogPerms = document.GetCatalog().GetPdfObject().GetAsDictionary(PdfName.Perms);
            if (catalogPerms != null) {
                urSignature = catalogPerms.GetAsDictionary(PdfName.UR3);
            }
            foreach (KeyValuePair<String, PdfFormField> entry in acroForm.GetAllFormFields()) {
                PdfDictionary fieldDict = entry.Value.GetPdfObject();
                if (!PdfName.Sig.Equals(fieldDict.Get(PdfName.FT))) {
                    continue;
                }
                PdfDictionary sigDict = fieldDict.GetAsDictionary(PdfName.V);
                if (sigDict == null) {
                    continue;
                }
                PdfSignature pdfSignature = new PdfSignature(sigDict);
                if (pdfSignature.GetContents() == null || pdfSignature.GetByteRange() == null) {
                    continue;
                }
                if (!pdfSignature.GetType().Equals(PdfName.DocTimeStamp) && sigDict != urSignature) {
                    containsCertificationOrApprovalSignature = true;
                    break;
                }
            }
            return containsCertificationOrApprovalSignature;
        }

        /// <summary>Get the rectangle associated to the provided widget.</summary>
        /// <param name="widget">PdfWidgetAnnotation to extract the rectangle from</param>
        /// <returns>Rectangle</returns>
        protected internal virtual Rectangle GetWidgetRectangle(PdfWidgetAnnotation widget) {
            return widget.GetRectangle().ToRectangle();
        }

        /// <summary>Get the page number associated to the provided widget.</summary>
        /// <param name="widget">PdfWidgetAnnotation from which to extract the page number</param>
        /// <returns>page number</returns>
        protected internal virtual int GetWidgetPageNumber(PdfWidgetAnnotation widget) {
            int pageNumber = 0;
            PdfDictionary pageDict = widget.GetPdfObject().GetAsDictionary(PdfName.P);
            if (pageDict != null) {
                pageNumber = document.GetPageNumber(pageDict);
            }
            else {
                for (int i = 1; i <= document.GetNumberOfPages(); i++) {
                    PdfPage page = document.GetPage(i);
                    if (!page.IsFlushed()) {
                        if (page.ContainsAnnotation(widget)) {
                            pageNumber = i;
                            break;
                        }
                    }
                }
            }
            return pageNumber;
        }

//\cond DO_NOT_DOCUMENT
        internal virtual PdfSignature CreateSignatureDictionary(bool includeDate) {
            PdfSignature dic = new PdfSignature();
            dic.SetReason(this.signerProperties.GetReason());
            dic.SetLocation(this.signerProperties.GetLocation());
            dic.SetSignatureCreator(this.signerProperties.GetSignatureCreator());
            dic.SetContact(this.signerProperties.GetContact());
            DateTime claimedSignDate = this.signerProperties.GetClaimedSignDate();
            if (includeDate && claimedSignDate != TimestampConstants.UNDEFINED_TIMESTAMP_DATE) {
                dic.SetDate(new PdfDate(claimedSignDate));
            }
            // time-stamp will over-rule this
            return dic;
        }
//\endcond

//\cond DO_NOT_DOCUMENT
        internal virtual byte[] EmbedMacTokenIntoSignatureContainer(byte[] signatureContainer) {
            using (Stream rangeStream = GetRangeStream()) {
                return EmbedMacTokenIntoSignatureContainer(signatureContainer, rangeStream, document);
            }
        }
//\endcond

//\cond DO_NOT_DOCUMENT
        internal static byte[] EmbedMacTokenIntoSignatureContainer(byte[] signatureContainer, Stream rangeStream, 
            PdfDocument document) {
            try {
                CMSContainer cmsContainer;
                if (JavaUtil.ArraysEquals(new byte[signatureContainer.Length], signatureContainer)) {
                    // Signature container is empty most likely due two two-step signing process.
                    // We will create blank signature container in order to add MAC in there.
                    cmsContainer = new CMSContainer();
                    SignerInfo signerInfo = new SignerInfo();
                    String digestAlgorithmOid = DigestAlgorithms.GetAllowedDigest(DigestAlgorithms.SHA256);
                    signerInfo.SetDigestAlgorithm(new AlgorithmIdentifier(digestAlgorithmOid));
                    signerInfo.SetSignatureAlgorithm(new AlgorithmIdentifier(OID.RSA));
                    signerInfo.SetSignature("This is a placeholder signature. It's value shall be replaced with a real signature."
                        .GetBytes(System.Text.Encoding.UTF8));
                    cmsContainer.SetSignerInfo(signerInfo);
                }
                else {
                    cmsContainer = new CMSContainer(signatureContainer);
                }
                // If MAC is in the signature already, we regenerate it anyway.
                cmsContainer.GetSignerInfo().RemoveUnSignedAttribute(ID_ATTR_PDF_MAC_DATA);
                IAsn1EncodableVector unsignedVector = FACTORY.CreateASN1EncodableVector();
                document.DispatchEvent(new SignatureContainerGenerationEvent(unsignedVector, cmsContainer.GetSignerInfo().
                    GetSignatureData(), rangeStream));
                if (FACTORY.CreateDERSequence(unsignedVector).Size() != 0) {
                    IAsn1Sequence sequence = FACTORY.CreateASN1Sequence(FACTORY.CreateDERSequence(unsignedVector).GetObjectAt(
                        0));
                    cmsContainer.GetSignerInfo().AddUnSignedAttribute(new CmsAttribute(FACTORY.CreateASN1ObjectIdentifier(sequence
                        .GetObjectAt(0)).GetId(), sequence.GetObjectAt(1).ToASN1Primitive()));
                    return cmsContainer.Serialize();
                }
            }
            catch (Exception exception) {
                throw new PdfException(SignExceptionMessageConstant.NOT_POSSIBLE_TO_EMBED_MAC_TO_SIGNATURE, exception);
            }
            return signatureContainer;
        }
//\endcond

        private static String GetSignerName(IX509Certificate certificate) {
            String name = null;
            CertificateInfo.X500Name x500name = CertificateInfo.GetSubjectFields(certificate);
            if (x500name != null) {
                name = x500name.GetField("CN");
                if (name == null) {
                    name = x500name.GetField("E");
                }
            }
            return name == null ? "" : name;
        }

        private void UpdateFieldName() {
            if (signerProperties.GetFieldName() != null) {
                PdfFormField field = acroForm.GetField(signerProperties.GetFieldName());
                if (field != null) {
                    if (!PdfName.Sig.Equals(field.GetFormType())) {
                        throw new ArgumentException(SignExceptionMessageConstant.FIELD_TYPE_IS_NOT_A_SIGNATURE_FIELD_TYPE);
                    }
                    if (field.GetValue() != null) {
                        throw new ArgumentException(SignExceptionMessageConstant.FIELD_ALREADY_SIGNED);
                    }
                    IList<PdfWidgetAnnotation> widgets = field.GetWidgets();
                    if (!widgets.IsEmpty()) {
                        PdfWidgetAnnotation widget = widgets[0];
                        this.signerProperties.SetPageRect(GetWidgetRectangle(widget));
                        this.signerProperties.SetPageNumber(GetWidgetPageNumber(widget));
                    }
                }
                else {
                    // Do not allow dots for new fields
                    // For existing fields dots are allowed because there it might be fully qualified name
                    if (signerProperties.GetFieldName().IndexOf('.') >= 0) {
                        throw new ArgumentException(SignExceptionMessageConstant.FIELD_NAMES_CANNOT_CONTAIN_A_DOT);
                    }
                }
            }
            else {
                this.signerProperties.SetFieldName(GetNewSigFieldName());
            }
        }

        private bool IsDocumentPdf2() {
            return document.GetPdfVersion().CompareTo(PdfVersion.PDF_2_0) >= 0;
        }

        protected internal virtual void ApplyAccessibilityProperties(PdfFormField formField, IAccessibleElement modelElement
            , PdfDocument pdfDocument) {
            if (!pdfDocument.IsTagged()) {
                return;
            }
            AccessibilityProperties properties = modelElement.GetAccessibilityProperties();
            String alternativeDescription = properties.GetAlternateDescription();
            if (alternativeDescription != null && !String.IsNullOrEmpty(alternativeDescription)) {
                formField.SetAlternativeName(alternativeDescription);
            }
        }

        private void ApplyDefaultPropertiesForTheNewField(PdfSignatureFormField sigField) {
            SignatureFieldAppearance formFieldElement = GetSignatureAppearance();
            PdfFormAnnotation annotation = sigField.GetFirstFormAnnotation();
            annotation.SetFormFieldElement(formFieldElement);
            // Apply default field properties:
            sigField.GetWidgets()[0].SetHighlightMode(PdfAnnotation.HIGHLIGHT_NONE);
            sigField.SetJustification(formFieldElement.GetProperty<TextAlignment?>(Property.TEXT_ALIGNMENT));
            Object retrievedFont = formFieldElement.GetProperty<Object>(Property.FONT);
            if (retrievedFont is PdfFont) {
                sigField.SetFont((PdfFont)retrievedFont);
            }
            UnitValue fontSize = formFieldElement.GetProperty<UnitValue>(Property.FONT_SIZE);
            if (fontSize != null && fontSize.IsPointValue()) {
                sigField.SetFontSize(fontSize.GetValue());
            }
            TransparentColor color = formFieldElement.GetProperty<TransparentColor>(Property.FONT_COLOR);
            if (color != null) {
                sigField.SetColor(color.GetColor());
            }
            BorderStyleUtil.ApplyBorderProperty(formFieldElement, annotation);
            Background background = formFieldElement.GetProperty<Background>(Property.BACKGROUND);
            ApplyAccessibilityProperties(sigField, formFieldElement, document);
            if (background != null) {
                sigField.GetFirstFormAnnotation().SetBackgroundColor(background.GetColor());
            }
        }

        private void SetContent() {
            if (this.signerProperties.GetPageRect() == null || this.signerProperties.GetPageRect().GetWidth() == 0 || 
                this.signerProperties.GetPageRect().GetHeight() == 0) {
                return;
            }
            this.signerProperties.GetSignatureAppearance().SetContent(GenerateSignatureText());
        }

        private SignedAppearanceText GenerateSignatureText() {
            SignedAppearanceText signedAppearanceText = new SignedAppearanceText();
            FillInAppearanceText(signedAppearanceText);
            return signedAppearanceText;
        }

        private void PopulateExistingModelElement() {
            this.signerProperties.GetSignatureAppearance().SetSignerName(signerName);
            SignedAppearanceText appearanceText = this.signerProperties.GetSignatureAppearance().GetSignedAppearanceText
                ();
            if (appearanceText != null) {
                FillInAppearanceText(appearanceText);
            }
        }

        private void FillInAppearanceText(SignedAppearanceText appearanceText) {
            appearanceText.SetSignedBy(signerName);
            DateTime claimedSignDate = this.signerProperties.GetClaimedSignDate();
            if (claimedSignDate != TimestampConstants.UNDEFINED_TIMESTAMP_DATE) {
                appearanceText.SetSignDate(claimedSignDate);
            }
            String reason = signerProperties.GetReason();
            bool setReason = appearanceText.GetReasonLine() != null && String.IsNullOrEmpty(appearanceText.GetReasonLine
                ());
            if (setReason && reason != null && !String.IsNullOrEmpty(reason)) {
                appearanceText.SetReasonLine("Reason: " + reason);
            }
            String location = signerProperties.GetLocation();
            bool setLocation = appearanceText.GetLocationLine() != null && String.IsNullOrEmpty(appearanceText.GetLocationLine
                ());
            if (setLocation && location != null && !String.IsNullOrEmpty(location)) {
                appearanceText.SetLocationLine("Location: " + location);
            }
        }

        private String GetFieldName() {
            UpdateFieldName();
            return signerProperties.GetFieldName();
        }

        /// <summary>An interface to retrieve the signature dictionary for modification.</summary>
        public interface ISignatureEvent {
            /// <summary>Allows modification of the signature dictionary.</summary>
            /// <param name="sig">The signature dictionary</param>
            void GetSignatureDictionary(PdfSignature sig);
        }

//\cond DO_NOT_DOCUMENT
        internal class SignatureApplier {
            private readonly PdfDocument document;

            private readonly PdfReader reader;

            private readonly String fieldName;

            private readonly Stream outs;

            private IRandomAccessSource readerSource;

            private long[] gaps;

            public SignatureApplier(PdfReader reader, String fieldName, Stream outs) {
                this.reader = reader;
                this.fieldName = fieldName;
                this.outs = outs;
                this.document = null;
            }

            public SignatureApplier(PdfDocument document, String fieldName, Stream outs) {
                this.document = document;
                this.fieldName = fieldName;
                this.outs = outs;
                this.reader = null;
            }

            public virtual void Apply(PdfSigner.ISignatureDataProvider signatureDataProvider) {
                StampingProperties properties = new StampingProperties().PreserveEncryption();
                properties.RegisterDependency(typeof(IMacContainerLocator), new SignatureMacContainerLocator());
                // This IdleOutputStream writer does nothing and only required to be able to apply MAC if needed.
                using (PdfWriter dummyWriter = new PdfWriter(new IdleOutputStream())) {
                    if (document == null) {
                        using (PdfDocument newDocument = new PdfDocument(reader, dummyWriter, properties)) {
                            Apply(newDocument, signatureDataProvider);
                        }
                    }
                    else {
                        RandomAccessFileOrArray raf = document.GetReader().GetSafeFile();
                        WindowRandomAccessSource source = new WindowRandomAccessSource(raf.CreateSourceView(), 0, raf.Length());
                        using (Stream inputStream = new RASInputStream(source)) {
                            using (PdfReader newReader = new PdfReader(inputStream, document.GetReader().GetPropertiesCopy())) {
                                using (PdfDocument newDocument = new PdfDocument(newReader, dummyWriter, properties)) {
                                    Apply(newDocument, signatureDataProvider);
                                }
                            }
                        }
                    }
                }
            }

//\cond DO_NOT_DOCUMENT
            internal virtual void Apply(PdfDocument document, PdfSigner.ISignatureDataProvider signatureDataProvider) {
                SignatureUtil signatureUtil = new SignatureUtil(document);
                PdfSignature signature = signatureUtil.GetSignature(fieldName);
                if (signature == null) {
                    throw new PdfException(SignExceptionMessageConstant.THERE_IS_NO_FIELD_IN_THE_DOCUMENT_WITH_SUCH_NAME).SetMessageParams
                        (fieldName);
                }
                if (!signatureUtil.SignatureCoversWholeDocument(fieldName)) {
                    throw new PdfException(SignExceptionMessageConstant.SIGNATURE_WITH_THIS_NAME_IS_NOT_THE_LAST_IT_DOES_NOT_COVER_WHOLE_DOCUMENT
                        ).SetMessageParams(fieldName);
                }
                PdfArray b = signature.GetByteRange();
                gaps = b.ToLongArray();
                readerSource = document.GetReader().GetSafeFile().CreateSourceView();
                int spaceAvailable = (int)(gaps[2] - gaps[1]) - 2;
                if ((spaceAvailable & 1) != 0) {
                    throw new ArgumentException("Gap is not a multiple of 2");
                }
                byte[] signedContent = signatureDataProvider(this);
                if (document.GetDiContainer().GetInstance<IMacContainerLocator>().IsMacContainerLocated()) {
                    RandomAccessSourceFactory fac = new RandomAccessSourceFactory();
                    IRandomAccessSource randomAccessSource = fac.CreateRanged(readerSource, gaps);
                    RASInputStream signedDocumentStream = new RASInputStream(randomAccessSource);
                    signedContent = EmbedMacTokenIntoSignatureContainer(signedContent, signedDocumentStream, document);
                }
                spaceAvailable /= 2;
                if (spaceAvailable < signedContent.Length) {
                    throw new PdfException(SignExceptionMessageConstant.AVAILABLE_SPACE_IS_NOT_ENOUGH_FOR_SIGNATURE);
                }
                StreamUtil.CopyBytes(readerSource, 0, gaps[1] + 1, outs);
                ByteBuffer bb = new ByteBuffer(spaceAvailable * 2);
                foreach (byte bi in signedContent) {
                    bb.AppendHex(bi);
                }
                int remain = (spaceAvailable - signedContent.Length) * 2;
                for (int k = 0; k < remain; ++k) {
                    bb.Append((byte)48);
                }
                byte[] bbArr = bb.ToByteArray();
                outs.Write(bbArr);
                StreamUtil.CopyBytes(readerSource, gaps[2] - 1, gaps[3] + 1, outs);
                document.Close();
            }
//\endcond

            public virtual Stream GetDataToSign() {
                return new RASInputStream(new RandomAccessSourceFactory().CreateRanged(readerSource, gaps));
            }
        }
//\endcond

        internal delegate byte[] ISignatureDataProvider(PdfSigner.SignatureApplier applier);

        private class PdfSignerDocument : PdfDocument {
            public PdfSignerDocument(PdfReader reader, PdfWriter writer, StampingProperties properties)
                : base(reader, writer, properties) {
                if (GetConformance().IsPdfA()) {
                    PdfAChecker checker = PdfADocument.GetCorrectCheckerFromConformance(GetConformance().GetAConformance());
                    ValidationContainer validationContainer = new ValidationContainer();
                    validationContainer.AddChecker(checker);
                    GetDiContainer().Register(typeof(ValidationContainer), validationContainer);
                    this.pdfPageFactory = new PdfAPageFactory(checker);
                    this.documentInfoHelper = new PdfADocumentInfoHelper(this);
                    this.defaultFontStrategy = new PdfADefaultFontStrategy(this);
                    SetFlushUnusedObjects(true);
                }
            }
        }
    }
}
