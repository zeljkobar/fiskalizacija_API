using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Summa.Fiscal.Infrastructure.Fiscalization.V5;

public interface IFiscalXmlSchemaValidatorV5
{
    FiscalXmlValidationResultV5 Validate(XDocument document);
}

public sealed record FiscalXmlValidationErrorV5(
    XmlSeverityType Severity,
    string Message,
    int LineNumber,
    int LinePosition);

public sealed record FiscalXmlValidationResultV5(
    IReadOnlyCollection<FiscalXmlValidationErrorV5> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed class FiscalXmlSchemaValidatorV5 : IFiscalXmlSchemaValidatorV5
{
    private const string MinimalXmlDsigSchema = """
        <?xml version="1.0" encoding="utf-8"?>
        <schema xmlns="http://www.w3.org/2001/XMLSchema"
                targetNamespace="http://www.w3.org/2000/09/xmldsig#"
                xmlns:ds="http://www.w3.org/2000/09/xmldsig#"
                elementFormDefault="qualified">
          <element name="Signature">
            <complexType>
              <sequence>
                <any minOccurs="0" maxOccurs="unbounded" processContents="skip" />
              </sequence>
              <anyAttribute processContents="skip" />
            </complexType>
          </element>
        </schema>
        """;

    private readonly XmlSchemaSet _schemas;

    public FiscalXmlSchemaValidatorV5(string officialXsdPath)
    {
        if (string.IsNullOrWhiteSpace(officialXsdPath))
            throw new ArgumentException("XSD putanja je obavezna.", nameof(officialXsdPath));
        if (!File.Exists(officialXsdPath))
            throw new FileNotFoundException("Zvanični PU XSD nije pronađen.", officialXsdPath);

        _schemas = BuildSchemaSet(officialXsdPath);
    }

    public FiscalXmlValidationResultV5 Validate(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<FiscalXmlValidationErrorV5>();
        document.Validate(
            _schemas,
            (_, args) =>
            {
                var exception = args.Exception;
                errors.Add(new(
                    args.Severity,
                    args.Message,
                    exception?.LineNumber ?? 0,
                    exception?.LinePosition ?? 0));
            },
            true);

        return new(errors);
    }

    private static XmlSchemaSet BuildSchemaSet(string officialXsdPath)
    {
        var schemaSet = new XmlSchemaSet
        {
            XmlResolver = null
        };

        using (var signatureReader = XmlReader.Create(new StringReader(MinimalXmlDsigSchema)))
        {
            schemaSet.Add(PuFiscalContractV5.XmlDsigNamespace, signatureReader);
        }

        var officialSchema = XDocument.Load(officialXsdPath, LoadOptions.PreserveWhitespace);
        XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
        var signatureImport = officialSchema.Root?
            .Elements(xsd + "import")
            .SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute("namespace"),
                    PuFiscalContractV5.XmlDsigNamespace,
                    StringComparison.Ordinal));
        signatureImport?.Attribute("schemaLocation")?.Remove();

        using var schemaReader = officialSchema.CreateReader();
        schemaSet.Add(PuFiscalContractV5.SchemaNamespace, schemaReader);
        schemaSet.Compile();
        return schemaSet;
    }
}
