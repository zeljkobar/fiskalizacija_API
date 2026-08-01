# AGENTS.md — Uputstvo za Codex i AI agente

Ovaj fajl je obavezan početni dokument za svaki AI agent koji radi na projektu.

## Glavno pravilo

Prije pisanja koda, agent mora pročitati:

1. `README.md`
2. `00_BLUEPRINT/00_SUMMA_BLUEPRINT.md`
3. `01_ARCHITECTURE/SYSTEM_ARCHITECTURE.md`
4. `01_ARCHITECTURE/API_STANDARD.md`
5. odgovarajući modul u kojem radi

## Stil implementacije

- Ne praviti brza, privremena i neobjašnjena rješenja.
- Ne duplirati logiku.
- Ne koristiti nazive kao `Helper`, `Utils`, `Service2`, `NewService`.
- Svaka poslovna logika mora biti u Application/Domain sloju, ne u Controller-u.
- Svaka kritična operacija mora imati audit log.
- Sve fiskalne poruke prema Poreskoj upravi moraju biti sačuvane u request/response logu.
- Sve greške moraju biti strukturisane.
- Svaki endpoint mora podržati correlation id.
- Kod mora biti pripremljen za testiranje.

## Zabranjeno

- Direktno fiskalizovati račun bez validacije.
- Brisati fiskalne dokumente iz baze.
- Čuvati privatne ključeve i sertifikate kao običan tekst.
- Miješati fiskalnu logiku sa UI logikom.
- Miješati računovodstvenu logiku sa transportnim SOAP/XML kodom.

## Prioriteti

1. Tačnost fiskalne i računovodstvene logike.
2. Sigurnost sertifikata i komunikacije.
3. Audit i trag svake operacije.
4. Stabilnost i mogućnost oporavka nakon greške.
5. Čist i održiv kod.

