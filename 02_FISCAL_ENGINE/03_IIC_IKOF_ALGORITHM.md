# 03_IIC_IKOF_ALGORITHM.md

## Svrha

Ovaj dokument definiše implementacioni pristup za generisanje IKOF/IIC koda računa u `SUMMA_FISCAL_PLATFORM`.

IKOF/IIC je jedan od najkritičnijih djelova fiskalizacije. Ako algoritam nije tačan, PU neće prihvatiti račun ili račun neće biti pravilno provjerljiv.

## Pravilo izvora istine

Tačan algoritam, redosljed polja, separator, encoding, hash algoritam i potpisivanje moraju biti preuzeti iz zvanične tehničke specifikacije Poreske uprave v5.

Ovaj dokument definiše arhitekturu i kontrolne tačke, ali ne smije zamijeniti zvanični algoritam.

## Cilj implementacije

Implementacija mora biti:

```text
- deterministička
- testabilna
- izolovana od ostatka sistema
- pokrivena unit testovima sa zvaničnim primjerima
- verzionisana po fiskalnom servisu
```

## Ključna pravila

1. Isti ulaz uvijek mora dati isti IKOF/IIC.
2. Promjena bilo kojeg relevantnog polja mora dati drugačiji IKOF/IIC.
3. Algoritam ne smije zavisiti od regionalnih podešavanja servera.
4. Decimalni iznosi moraju biti formatirani tačno po specifikaciji.
5. Datum i vrijeme moraju biti formatirani tačno po specifikaciji.
6. Private key se koristi samo u kontrolisanom signing servisu.
7. Sirovi string za potpis/hash mora se čuvati u audit logu, ako to ne krši bezbjednosna pravila.

## Predloženi interfejs

```csharp
public interface IIicGenerator
{
    IicGenerationResult Generate(FiscalInvoice invoice, X509Certificate2 certificate);
}

public sealed class IicGenerationResult
{
    public string Iic { get; init; }
    public string InputString { get; init; }
    public string AlgorithmVersion { get; init; }
    public string CertificateThumbprint { get; init; }
    public string HashHex { get; init; }
}
```

## Interni koraci

Generisanje se dijeli u korake:

```text
1. Priprema domenskog modela računa
2. Validacija obaveznih podataka
3. Normalizacija vrijednosti
4. Formatiranje vrijednosti po specifikaciji
5. Formiranje canonical input stringa
6. Potpisivanje / hash po algoritmu iz specifikacije
7. Konverzija rezultata u finalni IKOF/IIC format
8. Čuvanje rezultata
```

## Potvrđeni zvanični algoritam

Prema poglavlju 4.3.2 i C# primjeru iz zvanične tehničke specifikacije v5,
kanonski ulaz se sastoji od sljedećih vrijednosti ovim tačnim redosljedom:

```text
PIB/JMB izdavaoca
|datum i vrijeme izdavanja
|redni broj računa
|kod poslovne jedinice
|kod ENU/TCR
|kod softvera
|ukupna cijena
```

Primjer:

```text
12345678|2019-06-12T17:05:43+02:00|9952|bb123bb123|cc123cc123|ss123ss123|99.01
```

Pravila:

1. Vrijednosti se kodiraju kao UTF-8.
2. Separator je znak `|` (decimalni UTF-8 kod 124).
3. Kanonski ulaz se potpisuje RSA/SHA-256 uz RSASSA-PKCS-v1_5 padding.
4. Sirovi bajtovi potpisa pretvaraju se u velika heksadecimalna slova i čine
   `IICSignature` (512 znakova za RSA ključ od 2048 bita).
5. MD5 se računa nad sirovim bajtovima RSA potpisa, ne nad heksadecimalnim
   tekstom.
6. MD5 rezultat se pretvara u velika heksadecimalna slova i čini `IIC`
   (32 znaka).

Broj računa koji ulazi u IKOF je `InvOrdNum`, odnosno redni broj, a ne kompletan
formatirani `InvNum`.

## Podaci koji se tipično koriste

Tačan spisak mora se potvrditi iz specifikacije. U praksi su relevantne sljedeće grupe podataka:

```text
- PIB / TIN izdavaoca
- datum i vrijeme izdavanja
- broj računa
- oznaka poslovnog prostora
- oznaka ENU / uređaja
- ukupan iznos računa
- eventualno redni broj ili drugi identifikator propisan specifikacijom
```

## Normalizacija decimalnih vrijednosti

Nikad ne koristiti:

```csharp
double
float
amount.ToString()
```

Koristiti:

```csharp
amount.ToString("F2", CultureInfo.InvariantCulture)
```

ili format iz zvanične specifikacije ako je drugačiji.

## Normalizacija datuma

Nikad ne koristiti server lokalnu kulturu.

Koristiti eksplicitni format iz specifikacije, npr. kroz centralnu funkciju:

```csharp
public interface IFiscalDateTimeFormatter
{
    string FormatForIic(DateTimeOffset issuedAt);
    string FormatForXml(DateTimeOffset issuedAt);
}
```

## Audit za IKOF/IIC

Tabela `fiscal_iic_logs`:

```text
id
invoice_id
company_id
algorithm_version
input_string
input_hash
certificate_thumbprint
iic
created_at
```

Ovo je važno za kasniju forenziku: ako PU odbije račun, mora se vidjeti tačno koji string je potpisan.

## Testiranje algoritma

Obavezni testovi:

```text
1. Zvanični primjer iz PU dokumentacije mora dati isti IKOF/IIC.
2. Isti račun generisan dva puta mora dati isti IKOF/IIC.
3. Promjena iznosa za 0.01 mora promijeniti IKOF/IIC.
4. Promjena vremena mora promijeniti IKOF/IIC, ako vrijeme ulazi u algoritam.
5. Formatiranje decimalnog iznosa ne smije zavisiti od jezika OS-a.
6. Sertifikat bez privatnog ključa mora vratiti jasnu grešku.
7. Nevažeći sertifikat mora vratiti jasnu grešku.
```

## Greške

```text
IIC_CERTIFICATE_NOT_FOUND
IIC_CERTIFICATE_HAS_NO_PRIVATE_KEY
IIC_INVALID_INVOICE_NUMBER
IIC_INVALID_ISSUE_DATE
IIC_INVALID_TOTAL_AMOUNT
IIC_INPUT_FORMAT_ERROR
IIC_SIGNING_FAILED
IIC_UNSUPPORTED_ALGORITHM_VERSION
```

## Codex zadatak

Implementirati `IicGeneratorV5`, ali ne izmišljati algoritam. Prvo iz zvanične specifikacije prepisati tačan algoritam u komentar klase i u unit test dodati zvanični primjer.

```text
src/Summa.Fiscal.Infrastructure/Iic/IicGeneratorV5.cs
tests/Summa.Fiscal.UnitTests/Iic/IicGeneratorV5Tests.cs
```
