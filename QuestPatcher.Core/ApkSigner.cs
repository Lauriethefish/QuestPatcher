/*
 * All-C# APK signing code was taken from emulamer's Apkifier library: https://github.com/emulamer/Apkifier/blob/master/Apkifier.cs
MIT License

Copyright (c) 2019 emulamer

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Cms;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.Utilities.IO.Pem;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Store;
using QuestPatcher.Core.Patching;
using PemReader = Org.BouncyCastle.OpenSsl.PemReader;

namespace QuestPatcher.Core
{
    public class ApkSigner
    {
        private const string PatchingCertificatePem = @"-----BEGIN CERTIFICATE-----
MIICpjCCAY6gAwIBAgIIcmOVkuI/DbUwDQYJKoZIhvcNAQELBQAwEjEQMA4GA1UE
AwwHVW5rbm93bjAgFw0xMTA5MjkwMDAwMDBaGA8yMDcxMDkyOTAwMDAwMFowEjEQ
MA4GA1UEAwwHVW5rbm93bjCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEB
AIb3U/N3xLTsmde+nX2Z7ABc+EMsK92w5N0rn/ynAE+3Qyvb4nY6qFBsP1LqP1uN
ZyMoZOm+8MqSW0zFsEABEzkWmu1Ahecl6JIDqjSbamj+IkZZLFICUg00UIw5XJ1/
uq2jX2hknc/qPrGNsvltgZ5NDwIc/eNJ7Sb1b8PYD1rHWMEvxkdDmW41EUP35j5N
ATJX2NjQC3QZAslbkT890TrlwNWbexa1YypSSe31hjaTYVc8ubsoacGq/dSxAkOf
EKYf1+U+z0Vdxu76wSnfO7H/SXPYc4ToNzzoqk0ko9LBzjTqle1sHEJBCKRBbMKt
Qylz4rjyMobvgIFkPqFy6d8CAwEAATANBgkqhkiG9w0BAQsFAAOCAQEAMNTQo9lg
bvHnp1Ot4g1UgjpSDu52BKdAB0eaeR/3Rtm+E0E+jUMXSI70im4PxbN+eOmTG3NC
o0nO/FLQUw3j3o3kmON4VlPapGsDpKe2rHbL+5HySPbSjkGpwTTGPVzzfhv9dUD6
l97QIB5cmvRH3T9CP/8c+erOARBF2kGitdNTtyUxvQsl/xaiKAnuaE7Ub0YmpsZQ
e1EiJ9LNwF92YvK3dWP9cBKOKnxQEAcSgugGWWIbiCWF9KHLUWYvT2Gv1tgl+kvE
/ZUie++OqnFEjPeWDTsbpiJXD1sKFUp3iCf970mgLMfXYwkiRxwicYFny0tu90wF
Nbzwy1zKhUC80w==
-----END CERTIFICATE-----
-----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEAhvdT83fEtOyZ176dfZnsAFz4Qywr3bDk3Suf/KcAT7dDK9vi
djqoUGw/Uuo/W41nIyhk6b7wypJbTMWwQAETORaa7UCF5yXokgOqNJtqaP4iRlks
UgJSDTRQjDlcnX+6raNfaGSdz+o+sY2y+W2Bnk0PAhz940ntJvVvw9gPWsdYwS/G
R0OZbjURQ/fmPk0BMlfY2NALdBkCyVuRPz3ROuXA1Zt7FrVjKlJJ7fWGNpNhVzy5
uyhpwar91LECQ58Qph/X5T7PRV3G7vrBKd87sf9Jc9hzhOg3POiqTSSj0sHONOqV
7WwcQkEIpEFswq1DKXPiuPIyhu+AgWQ+oXLp3wIDAQABAoIBAATKc2dutpkkK5Mr
R/81CdnlcvE8zdMmZqlXKsGsJ+hXKAI+4ZCyz6td0aL0KmA/P7b8GxY/rzUcRt4N
x7jYkOxzhKJWqlTPKqyW3AwsAXX392gp3YHiZTPkydXVv2zeeNZAY1rwSg3Jpy9k
SUMYrljb1rmLepOpLyA1PCIRNvz7jXreXVTZlaOmUUXT4tGeJNXgMseJ41szzBmB
5Ouro4goqx2jTkGx6qX/RnvIo/hp0ykHSQOtg3F1tmcYi5qlKwmoFln3pRhN27Ed
JUOaAV7adv09hyM6yRosg0A47abFHIBBCqgvdjqNk+orboUjhfPHabwt6x0imZ1k
v4iwUNkCgYEA9dO7l1IqGJ1fSlQwwy5HRB8mb5s488G1sUTmxR6ByPr2gf7h6FmX
vG1yQZ3A6OLVFHkFRa++bGdZ3ngW6vnQhmfpAVaAjg3EkdlAN1eVyEfACkkoYV20
D4PmM07B9aOGKXK0KpvwYh8GlZDKuKIC8QymLlDxOw/UJImFn/4daIsCgYEAjI0l
OQ7SlKvdalYs0fpPo3gZmygFeosfNFJ1xwDxhzoBvUhkHfHho6qBj7wb2E4UjHQC
67QgnQe60SQnpB/LfmCk9HvQ4dB0kCGhoHHuhUH9Kf9PX6auKGySNZ6p7gG8xOy9
dBjMH75gJK3H/LsW7LERCDHmaefS53f1QpfCWn0CgYBxxy8TKa9cNzKMl4z+OaQ4
jmZez6w7fhPXWXmqEKWnXSjNICh1P0pwpwN0BUztPVe8IwtipqXvTKKWymRpG3j9
TIjW2q+jkBHEI5aKRtqHmVX0LMoozpLxf24Dn1c8lxQYiQOEmSpYb92/SgXaEPpl
kSI1W7dbS8c3pgMX+yinYwKBgBhnTWY5x6BesuQKsF+I+ZjlenSxHzpmu3VHOAHk
jQswrCqkThXQ8J+NNE+zlpYZAIJehj9MmDkLpYk4oNVjW97Ggv2cHemHWyXHYRvN
jF+A1KcdGDgAZc7JAx3iPZkAnjkG7eIhiBee42ya69Va2qEgIVft6hbLVJgyANie
JvW1AoGBANX/7ZpHZO6UKb8KBs81aMn1mw478p3R4BrjaBh/9Cmh98UjxzgNo9+K
QwA97QgLhZd5HjLpZlEzV45gO4VakAAnXDtCEWEMPy2Pp/Oo+kw5sznsUe9Dk6A0
llAY8xXVMiYeyHboXxDPOCH8y1TgEW0Nc2cnnCKOuji2waIwrVwR
-----END RSA PRIVATE KEY-----";
        
        private static readonly Encoding Encoding = new UTF8Encoding();
        private static readonly SHA1 Sha = SHA1.Create();

        /// <summary>
        /// Signs the signature file's content using the given certificate, and returns the RSA signature.
        /// </summary>
        /// <param name="signatureFileData">Content of the signature file to be signed</param>
        /// <param name="pemCertData">PEM data of the certificate and private key for signing</param>
        /// <returns>The RSA signature</returns>
        private byte[] GetSignature(byte[] signatureFileData, string pemCertData)
        {
            var (cert, privateKey) = LoadCertificate(pemCertData);
            
            var certStore = X509StoreFactory.Create("Certificate/Collection", new X509CollectionStoreParameters(new List<X509Certificate> { cert }));
            CmsSignedDataGenerator dataGen = new();
            dataGen.AddCertificates(certStore);
            dataGen.AddSigner(privateKey, cert, CmsSignedGenerator.EncryptionRsa, CmsSignedGenerator.DigestSha256);

            // Content is detached - i.e. not included in the signature block itself
            CmsProcessableByteArray detachedContent = new(signatureFileData);
            var signedContent = dataGen.Generate(detachedContent, false);

            // Get the signature in the proper ASN.1 structure for java to parse it properly.  Lots of trial and error
            var signerInfos = signedContent.GetSignerInfos();
            var signer = signerInfos.GetSigners().Cast<SignerInformation>().First();
            SignerInfo signerInfo = signer.ToSignerInfo();
            Asn1EncodableVector digestAlgorithmsVector = new();
            digestAlgorithmsVector.Add(new AlgorithmIdentifier(new DerObjectIdentifier("2.16.840.1.101.3.4.2.1"), DerNull.Instance));
            ContentInfo encapContentInfo = new(new DerObjectIdentifier("1.2.840.113549.1.7.1"), null);
            Asn1EncodableVector asnVector = new()
            {
                X509CertificateStructure.GetInstance(Asn1Object.FromByteArray(cert.GetEncoded()))
            };
            Asn1EncodableVector signersVector = new() {signerInfo.ToAsn1Object()};
            SignedData signedData = new(new DerSet(digestAlgorithmsVector), encapContentInfo, new BerSet(asnVector), null, new DerSet(signersVector));
            ContentInfo contentInfo = new(new DerObjectIdentifier("1.2.840.113549.1.7.2"), signedData);
            return contentInfo.GetDerEncoded();
        }

        /// <summary>
        /// Loads the certificate and private key from the given PEM data
        /// </summary>
        /// <param name="pemData"></param>
        /// <returns>The loaded certificate and private key</returns>
        /// <exception cref="System.Security.SecurityException">If the certificate or private key failed to load</exception>
        private (X509Certificate certificate, AsymmetricKeyParameter privateKey) LoadCertificate(string pemData)
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
        /// Signs the given APK with the QuestPatcher patching certificate.
        /// </summary>
        /// <param name="path">Path to the APK to sign</param>
        public async Task SignApkWithPatchingCertificate(string path)
        {
            await SignApk(path, PatchingCertificatePem);
        }

        /// <summary>
        /// Signs the archive with the given PEM certificate and private key. 
        /// </summary>
        /// <param name="path">Path to the APK to sign</param>
        /// <param name="pemData">PEM of the certificate and private key</param>
        public async Task SignApk(string path, string pemData)
        {
            //await using Stream manifestFile = apkArchive.CreateAndOpenEntry("META-INF/MANIFEST.MF");
            await using Stream manifestFile = new MemoryStream();
            //await using Stream signaturesFile = apkArchive.CreateAndOpenEntry("META-INF/BS.SF");
            await using Stream sigFileBody = new MemoryStream();
            await using(StreamWriter manifestWriter = OpenStreamWriter(manifestFile))
            {
                await manifestWriter.WriteLineAsync("Manifest-Version: 1.0");
                await manifestWriter.WriteLineAsync("Created-By: QuestPatcher");
                await manifestWriter.WriteLineAsync();
            }

            // Temporarily open the archive in order to calculate these hashes
            // This is done because opening all of the entries will cause them all to be recompressed if using ZipArchiveMode.Update, thus causing a long dispose time
            using(ZipArchive apkArchive = ZipFile.OpenRead(path))
            {
                foreach(ZipArchiveEntry entry in apkArchive.Entries.Where(entry =>
                    !entry.FullName.StartsWith("META-INF"))) // Skip other signature related files
                {
                    await WriteEntryHash(entry, manifestFile, sigFileBody);
                }
            }

            using(ZipArchive apkArchive = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                // Delete existing signature related files
                foreach(ZipArchiveEntry entry in apkArchive.Entries.Where(entry => entry.FullName.StartsWith("META-INF")).ToList())
                {
                    entry.Delete();
                }
                
                
                await using Stream signaturesFile = apkArchive.CreateAndOpenEntry("META-INF/BS.SF");
                await using Stream rsaFile = apkArchive.CreateAndOpenEntry("META-INF/BS.RSA");
                await using Stream manifestStream = apkArchive.CreateAndOpenEntry("META-INF/MANIFEST.MF");

                // Find the hash of the manifest
                manifestFile.Position = 0;
                byte[] manifestHash = await Sha.ComputeHashAsync(manifestFile);
                
                // Finally, copy it to the output file
                manifestFile.Position = 0;
                await manifestFile.CopyToAsync(manifestStream);
                
                // Write the signature information
                await using(StreamWriter signatureWriter = OpenStreamWriter(signaturesFile))
                {
                    await signatureWriter.WriteLineAsync("Signature-Version: 1.0");
                    await signatureWriter.WriteLineAsync($"SHA1-Digest-Manifest: {Convert.ToBase64String(manifestHash)}");
                    await signatureWriter.WriteLineAsync("Created-By: QuestPatcher");
                    await signatureWriter.WriteLineAsync();
                }
                
                // Copy the body of signatures for each file into the signature file
                sigFileBody.Position = 0;
                await sigFileBody.CopyToAsync(signaturesFile);
                signaturesFile.Position = 0;

                // Get the bytes in the signature file for signing
                await using MemoryStream sigFileMs = new();
                await signaturesFile.CopyToAsync(sigFileMs);

                // Sign the signature file, and save the signature
                byte[] keyFile = GetSignature(sigFileMs.ToArray(), pemData);
                await rsaFile.WriteAsync(keyFile);
            }
            
        }

        /// <summary>
        /// Writes the MANIFEST.MF and signature file hashes for the given entry
        /// </summary>
        private async Task WriteEntryHash(ZipArchiveEntry entry, Stream manifestStream, Stream signatureStream)
        {
            await using Stream sourceStream = entry.Open();
            byte[] hash = await Sha.ComputeHashAsync(sourceStream);

            // First write the digest of the file to a section of the manifest file
            await using MemoryStream sectStream = new();
            await using(StreamWriter sectWriter = OpenStreamWriter(sectStream))
            {
                await sectWriter.WriteLineAsync($"Name: {entry.FullName}");
                await sectWriter.WriteLineAsync($"SHA1-Digest: {Convert.ToBase64String(hash)}");
                await sectWriter.WriteLineAsync();
            }

            // Then write the hash for the section of the manifest file to the signature file
            sectStream.Position = 0;
            string sectHash = Convert.ToBase64String(await Sha.ComputeHashAsync(sectStream));
            await using(StreamWriter signatureWriter = OpenStreamWriter(signatureStream))
            {
                await signatureWriter.WriteLineAsync($"Name: {entry.FullName}");
                await signatureWriter.WriteLineAsync($"SHA1-Digest: {sectHash}");
                await signatureWriter.WriteLineAsync(); 
            }

            sectStream.Position = 0;
            await sectStream.CopyToAsync(manifestStream);
        }

        private StreamWriter OpenStreamWriter(Stream stream)
        {
            return new(stream, Encoding, 1024, true);
        }
        
        /// <summary>
        /// Creates a new X509 certificate and returns its data in PEM format.
        ///
        /// <see cref="PatchingCertificatePem"/> is generated using this method.
        /// </summary>
        public string GenerateNewCertificatePem()
        {
            
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);
            var certificateGenerator = new X509V3CertificateGenerator();
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);
            
            // TODO: Figure out ISignatureFactory to avoid these deprecated methods
#pragma warning disable 618
            certificateGenerator.SetSignatureAlgorithm("SHA256WithRSA");
#pragma warning restore 618
            var subjectDn = new X509Name("cn=Unknown");
            var issuerDn = subjectDn;
            certificateGenerator.SetIssuerDN(issuerDn);
            certificateGenerator.SetSubjectDN(subjectDn);
            certificateGenerator.SetNotBefore(DateTime.UtcNow.Date.AddYears(-10));
            certificateGenerator.SetNotAfter(DateTime.UtcNow.Date.AddYears(50));
            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();
            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // TODO: Figure out ISignatureFactory to avoid these deprecated methods
#pragma warning disable 618
            X509Certificate cert = certificateGenerator.Generate(subjectKeyPair.Private);
#pragma warning restore 618

            using var writer = new StringWriter();
            var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(writer);
                                
            pemWriter.WriteObject(new PemObject("CERTIFICATE", cert.GetEncoded()));
            pemWriter.WriteObject(subjectKeyPair.Private);
            return writer.ToString();
        }
    }
}
