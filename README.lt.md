# CatLu YNet

[Русский](README.md) | [English](README.en.md) | [Lietuvių](README.lt.md) | [עברית](README.he.md)

<img src="Assets/CatLuNet2.png" alt="CatLu YNet" width="96">

**„YouTube“ garsas ir vietiniai MP3 failai viename „Windows“ grotuve.**

„CatLu YNet“ leidžia klausytis „YouTube“ vaizdo įrašų ir grojaraščių garso be vaizdo ir naršyklės. Kurkite savo grojaraščius ir, jei norite, išsaugokite muziką diske.

## Atsisiuntimas

**[Naujausia versija: diegimo programa ir nešiojamasis ZIP paketas](https://github.com/Maksimasz/CatLuYNet/releases/latest)**

- Reikalinga „Windows x64“. Leidimo paketuose yra .NET ir MP3 įrankiai.
- Norėdami įdiegti, atsisiųskite ir paleiskite `CatLuYNet-…-Setup.exe`.
- Nešiojamajai versijai išskleiskite visą ZIP archyvą ir paleiskite `YouTubeRadio.exe`. Išlaikykite archyvo aplankų struktūrą.

## Galimybės

- Atskirų „YouTube“ vaizdo įrašų ir visų grojaraščių pridėjimas.
- Asmeninių grojaraščių kūrimas, pervadinimas ir redagavimas; kūrinių šalinimas bei rikiavimas.
- Nepriklausomas pasirinkto grojaraščio importas ir eksportas JSON formatu.
- Pasirenkamas vieno kūrinio arba viso grojaraščio atsisiuntimas MP3 formatu.
- Atsisiuntimo aplanko ir MP3 kokybės pasirinkimas; numatytoji kokybė — geriausia VBR.
- Atkuriant ir sklandžiai pereinant tarp kūrinių pirmenybė teikiama vietiniam failui. Jei jo nėra, naudojamas „YouTube“.
- Naujų MP3 failų pavadinimuose yra kūrinio pavadinimas; į failą įrašomi prieinami metaduomenys, šaltinio nuorodos ir viršelis. „Windows“ savybėse rodoma „YouTube“ nuoroda ir penkių žvaigždučių įvertinimas.
- Įjungus vizualizaciją, vietinio MP3 viršelis rodomas vietoje katino; jei viršelio nėra, lieka katinas.
- Ankstesnio kūrinio, atkūrimo, pauzės, sustabdymo, kito kūrinio ir garsumo valdikliai.
- Išvaizdos temos, rusų, anglų, hebrajų ir lietuvių sąsajos, laikrodis, kūrinio informacija ir mažasis grotuvas.
- Atnaujinimų tikrinimas paleidžiant programą ir kas 24 valandas.

## Kaip naudotis

1. Įklijuokite „YouTube“ nuorodą viršutiniame laukelyje ir ją išsaugokite. Vienam vaizdo įrašui pasirinkite savo grojaraštį, o pridėdami „YouTube“ grojaraštį suteikite jam pavadinimą.
2. Spustelėkite kūrinį, kad pradėtumėte atkūrimą. Mygtukas `✎` atveria grojaraščio redaktorių, `⚙` — nustatymus.
3. Jei norite vietinės kopijos, pridėdami nuorodą pažymėkite MP3 atsisiuntimą arba naudokite grojaraščio redaktoriaus atsisiuntimo mygtukus.
4. Pasirinkite išsaugojimo vietą. Joje sukuriamas aplankas grojaraščio pavadinimu. Atsisiuntimo lange rodomas visas kelias, o pasirinkta vieta įsimenama.
5. Per `⚙` pasirinkite MP3 kokybę: geriausią VBR, 320, 256, 192 arba 128 kbit/s.

Jau atsisiųsti failai neperrašomi. Kokybės pakeitimai taikomi naujiems atsisiuntimams; rezultatą riboja „YouTube“ šaltinio kokybė. Atšaukus užbaigti MP3 failai išlieka. „YouTube“ atkūrimui ir atsisiuntimui reikia interneto; išsaugotus MP3 galima klausyti neprisijungus. Nepasiekiamų vaizdo įrašų atsisiųsti negalima.

## Grojaraščiai ir duomenys

Nustatymuose eksportuokite vieną pasirinktą grojaraštį į JSON. Importuojamas grojaraštis pridedamas atskirai, net jei toks pavadinimas jau yra. Garso failai į JSON neįtraukiami. Nuoroda su `v=…` pridedama kaip vienas vaizdo įrašas, net jei joje yra `list` ir `index`.

Duomenys saugomi `%LOCALAPPDATA%\CatLuYNet`. Šalia bibliotekos ir nustatymų failų laikomos `.bak` atsarginės kopijos. Pašalinus kūrinį iš grojaraščio, jo MP3 failas diske išlieka.

## Kūrėjams

[Paleidimas iš pirminio kodo, patikros ir diegimo programos kūrimas — rusų kalba](docs/BUILD.md).

Programos kodas platinamas pagal [MIT licenciją](LICENSE). Trečiųjų šalių komponentams taikomos jų licencijos.
