# 04_DIGITAL_SIGNATURE.md

## Svrha

Ovaj dokument definiše pravila za digitalno potpisivanje u okviru fiskalizacije računa u Crnoj Gori.

Digitalni potpis se koristi za dokazivanje identiteta poreskog obveznika i integriteta poruke. Implementacija mora biti strogo usklađena sa zvaničnom tehničkom specifikacijom Poreske uprave.

## Osnovni principi

```text
- Privatni ključ nikad ne napušta sigurni kontekst.
- Sertifikat mora biti validan, aktivan i vezan za poreskog obveznika.
- Potpisivanje mora biti izolovano u posebnom servisu.
- Potpisani XML mora se validirati prije slanja.
- Sve greške potpisivanja moraju biti auditovane.
```

## Predložene klase

```csharp
public interface IFiscalXmlSigner
{
    SignedXmlResult Sign(FiscalXmlToSign xml, CertificateReference certificateReference);
}

public interface ISignatureValidator
{
    SignatureValidationResult Validate(string signedXml);
}

public sealed class SignedXmlResult
{
    public string SignedXml { get; init; }
    public string SignatureAlgorithm { get; init; }
    public string DigestAlgorithm { get; init; }
    public string CertificateThumbprint { get; init; }
    public DateTimeOffset SignedAt { get; init; }
}
```

## .NET tehnologije

Za C# implementaciju koristiti:

```text
System.Security.Cryptography.X509Certificates
System.Security.Cryptography.Xml
```

U zavisnosti od zahtjeva specifikacije, možda će biti potrebna dodatna biblioteka za XML canonicalization ili rad sa smart karticom/tokenom.

## Sertifikat

Sistem mora podržati barem:

```text
- PFX/P12 sertifikat uploadovan u sistem
- sertifikat instaliran u certificate store-u
- kasnije: HSM ili eksterni signing provider
```

Za MVP je prihvatljivo PFX/P12 uz strogu enkripciju i ograničen pristup.

## Pravila za PFX/P12

```text
1. Nikad ne čuvati lozinku u čistom tekstu.
2. PFX fajl čuvati enkriptovan.
3. Lozinku čuvati kroz secret manager ili enkriptovanu vrijednost.
4. Pristup sertifikatu logovati.
5. Rotacija sertifikata mora biti podržana.
6. Stari sertifikati se ne brišu, jer su potrebni za audit.
```

## Signing flow

```text
1. Učitati aktivni sertifikat firme.
2. Provjeriti datum važenja.
3. Provjeriti da sertifikat ima privatni ključ.
4. Provjeriti thumbprint i issuer.
5. Pripremiti XML za potpis.
6. Primijeniti canonicalization po specifikaciji.
7. Izvršiti digitalno potpisivanje.
8. Ubaciti Signature element na propisano mjesto.
9. Validirati potpis lokalno.
10. Sačuvati potpisani XML.
```

## Baza

Tabela `certificates`:

```text
id
company_id
name
thumbprint
serial_number
issuer
subject
valid_from
valid_to
storage_type
encrypted_blob_path
is_active
created_at
revoked_at
```

Tabela `certificate_access_logs`:

```text
id
certificate_id
company_id
operation
used_by_user_id
used_by_service
correlation_id
created_at
```

## Greške

```text
SIGN_CERTIFICATE_NOT_FOUND
SIGN_CERTIFICATE_EXPIRED
SIGN_CERTIFICATE_NOT_YET_VALID
SIGN_CERTIFICATE_NO_PRIVATE_KEY
SIGN_CERTIFICATE_PASSWORD_INVALID
SIGN_XML_INVALID_BEFORE_SIGNING
SIGN_XML_FAILED
SIGN_XML_INVALID_AFTER_SIGNING
SIGN_UNSUPPORTED_ALGORITHM
```

## Bezbjednost

Potpisivanje je jedan od najosjetljivijih djelova sistema. Zato:

```text
- signing servis ne smije biti dostupan javno
- API ne smije vraćati privatne podatke sertifikata
- logs ne smiju sadržati PFX lozinku
- production secrets ne smiju biti u Git-u
- pristup potpisivanju mora biti ograničen na sistemske procese
```

## Testovi

```text
- test potpisivanja zvaničnog XML primjera
- test validacije potpisa
- test sa isteklim sertifikatom
- test bez privatnog ključa
- test pogrešne lozinke
- test da se signed XML ne mijenja poslije potpisa
```
