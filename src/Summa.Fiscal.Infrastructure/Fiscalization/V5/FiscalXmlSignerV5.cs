using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public sealed record FiscalXmlSignatureResultV5(
    XDocument SignedDocument,
    string CertificateThumbprint,
    string SignatureMethod,
    string DigestMethod,
    bool SignatureVerified);

public interface IFiscalXmlSignerV5
{
    FiscalXmlSignatureResultV5 SignRequest(
        XDocument unsignedDocument,
        X509Certificate2 certificate);

    bool Verify(XDocument signedDocument, X509Certificate2 certificate);
}

public sealed class FiscalXmlSignerV5 : IFiscalXmlSignerV5
{
    public FiscalXmlSignatureResultV5 SignRequest(
        XDocument unsignedDocument,
        X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(unsignedDocument);
        ArgumentNullException.ThrowIfNull(certificate);

        using var privateKey = certificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException(
                "Sertifikat nema RSA privatni ključ potreban za XML potpis.");

        var document = ToXmlDocument(unsignedDocument);
        var root = document.DocumentElement
            ?? throw new InvalidOperationException("XML nema root element.");

        if (!string.Equals(root.GetAttribute("Id"), PuFiscalContractV5.RequestId, StringComparison.Ordinal))
            throw new InvalidOperationException("Root request mora imati Id=\"Request\".");

        RemoveExistingSignatures(root);

        var signedXml = new FiscalSignedXml(document)
        {
            SigningKey = privateKey
        };
        var signedInfo = signedXml.SignedInfo
            ?? throw new InvalidOperationException("SignedInfo nije inicijalizovan.");
        signedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference
        {
            Uri = $"#{PuFiscalContractV5.RequestId}",
            DigestMethod = SignedXml.XmlDsigSHA256Url
        };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform(false));
        reference.AddTransform(new XmlDsigExcC14NTransform(false));
        signedXml.AddReference(reference);

        var keyInfo = new KeyInfo();
        var x509Data = new KeyInfoX509Data();
        x509Data.AddCertificate(certificate);
        keyInfo.AddClause(x509Data);
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        var signature = signedXml.GetXml();
        root.AppendChild(document.ImportNode(signature, true));

        var signedDocument = XDocument.Parse(
            document.OuterXml,
            LoadOptions.PreserveWhitespace);
        var verified = Verify(signedDocument, certificate);

        return new(
            signedDocument,
            certificate.Thumbprint,
            SignedXml.XmlDsigRSASHA256Url,
            SignedXml.XmlDsigSHA256Url,
            verified);
    }

    public bool Verify(XDocument signedDocument, X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(signedDocument);
        ArgumentNullException.ThrowIfNull(certificate);

        var document = ToXmlDocument(signedDocument);
        var signatureElement = document
            .GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlElement>()
            .SingleOrDefault();

        if (signatureElement is null)
        {
            return false;
        }

        var signedXml = new FiscalSignedXml(document);
        signedXml.LoadXml(signatureElement);
        return signedXml.CheckSignature(certificate, true);
    }

    private static XmlDocument ToXmlDocument(XDocument source)
    {
        var document = new XmlDocument
        {
            PreserveWhitespace = true
        };

        using var reader = source.CreateReader();
        document.Load(reader);
        return document;
    }

    private static void RemoveExistingSignatures(XmlElement root)
    {
        var signatures = root
            .GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .OfType<XmlNode>()
            .ToArray();

        foreach (var signature in signatures)
        {
            signature.ParentNode?.RemoveChild(signature);
        }
    }

    private sealed class FiscalSignedXml(XmlDocument document) : SignedXml(document)
    {
        public override XmlElement? GetIdElement(XmlDocument? doc, string idValue)
        {
            if (doc is null)
            {
                return null;
            }

            var standardResult = base.GetIdElement(doc, idValue);
            if (standardResult is not null)
            {
                return standardResult;
            }

            return doc.SelectSingleNode($"//*[@Id='{idValue}']") as XmlElement;
        }
    }
}
