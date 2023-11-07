using System.Collections.Generic;
using System.IO;
using System.Linq;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.Collections;
using Org.BouncyCastle.X509;

namespace QuestPatcher.Zip
{
    internal static class SigningUtility
    {
        /// <summary>
        /// Loads the certificate and private key from PEM data
        /// </summary>
        /// <param name="pemData">The certificate and private key in PEM format</param>
        /// <returns>The loaded certificate and private key</returns>
        /// <exception cref="System.Security.SecurityException">If the certificate or private key failed to load</exception>
        internal static (X509Certificate certificate, AsymmetricKeyParameter privateKey) LoadCertificate(string pemData)
        {
            X509Certificate? cert = null;
            AsymmetricKeyParameter? privateKey = null;
            using (var reader = new StringReader(pemData))
            {
                // Iterate through the PEM objects until we find the public or private key
                var pemReader = new PemReader(reader);
                object pemObject;
                while ((pemObject = pemReader.ReadObject()) != null)
                {
                    cert ??= pemObject as X509Certificate;
                    privateKey ??= (pemObject as AsymmetricCipherKeyPair)?.Private;
                }
            }
            if (cert == null)
                throw new System.Security.SecurityException("Certificate could not be loaded from PEM data.");

            if (privateKey == null)
                throw new System.Security.SecurityException("Private Key could not be loaded from PEM data.");

            return (cert, privateKey);
        }

        /// <summary>
        /// Signs the given data using RSA with a SHA-256 digest, storing the result in PKCS1 format.
        /// </summary>
        /// <param name="data">The data to sign</param>
        /// <param name="privateKey">The private key to sign with</param>
        /// <returns>The signature in PKCS1 format</returns>
        internal static byte[] SignPKCS1(byte[] data, AsymmetricKeyParameter privateKey)
        {
            var signerType = SignerUtilities.GetSigner("SHA256WithRSA");
            signerType.Init(true, privateKey);
            signerType.BlockUpdate(data, 0, data.Length);

            return signerType.GenerateSignature();
        }

        /// <summary>
        /// Signs the given data using RSA with a SHA-256 digest, storing the result in PKCS7 format.
        /// </summary>
        /// <param name="data">The data to sign</param>
        /// <param name="privateKey">The private key to sign with</param>
        /// <returns>The signature in PKCS7 format</returns>
        internal static byte[] SignPKCS7(byte[] data, X509Certificate cert, AsymmetricKeyParameter privateKey)
        {
            var certStore = CollectionUtilities.CreateStore(new List<X509Certificate> { cert });
            var dataGen = new CmsSignedDataGenerator();
            dataGen.AddCertificates(certStore);
            dataGen.AddSigner(privateKey, cert, CmsSignedGenerator.EncryptionRsa, CmsSignedGenerator.DigestSha256);

            // Generate the signature
            var detachedContent = new CmsProcessableByteArray(data);
            var signedContent = dataGen.Generate(detachedContent, false); // Do not include the content inside the signature

            // Get the signature in the proper ASN.1 structure for java to parse it properly.
            var signerInfos = signedContent.GetSignerInfos();
            var signer = signerInfos.GetSigners().Cast<SignerInformation>().First();
            var signerInfo = signer.ToSignerInfo();

            var encapContentInfo = new ContentInfo(new DerObjectIdentifier("1.2.840.113549.1.7.1"), null);
            var asnVector = new Asn1EncodableVector()
            {
                X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(cert.GetEncoded()))
            };
            // Use a SHA-256 digest
            var digestAlgorithmsVector = new Asn1EncodableVector()
            {
                new AlgorithmIdentifier(new DerObjectIdentifier("2.16.840.1.101.3.4.2.1"), DerNull.Instance)
            };

            var signersVector = new Asn1EncodableVector() { signerInfo.ToAsn1Object() };
            var signedData = new SignedData(new DerSet(digestAlgorithmsVector), encapContentInfo, new BerSet(asnVector), null, new DerSet(signersVector));
            var contentInfo = new ContentInfo(new DerObjectIdentifier("1.2.840.113549.1.7.2"), signedData);

            return contentInfo.GetDerEncoded();
        }
    }
}
