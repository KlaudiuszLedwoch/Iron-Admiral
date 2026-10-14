IRON ADMIRAL 1.1
DIP The Great Game Extended (AI overhaul, one-click install)
dla Ultimate Admiral: Dreadnoughts (Steam, Windows)
============================================================

Iron Admiral to rozszerzona wersja Dreadnought Improvement Project
(DIP) "The Great Game": te same dane, mapa i nacje, do tego kilkadziesiąt zmian w
balansie, gospodarce i dyplomacji oraz nowy dodatek DIP_GG_Fixes, który
poprawia AI kampanii i AI bitewne w miejscach, do których sam plik CSV
nie sięga. Baza danych: DIP The Great Game 1.3.5 (lipiec 2026). Paczka jest
kompletna: MelonLoader 0.6.6, TweaksAndFixes 3.21.1, dane DIP ze
zmianami, kod źródłowy dodatku. Instalacja to jedno
kliknięcie, odinstalowanie przywraca stan sprzed instalacji.
Autor rozszerzenia: Klaudiusz Ledwoch (Steam: Klod76).
Nexus: https://www.nexusmods.com/ultimateadmiraldreadnoughts/mods/66
GitHub (kod źródłowy): https://github.com/KlaudiuszLedwoch/Iron-Admiral

--------------------------------------------------------------------------
DLACZEGO WARTO
--------------------------------------------------------------------------

AI, KTÓRE WALCZY JAK MARYNARKA WOJENNA, A NIE JAK SKRYPT
- Lekkie okręty AI (torpedowce, niszczyciele, lekkie krążowniki) idą do
  szarży torpedowej na pancerniki i ciężkie krążowniki, a nie krążą na
  skraju zasięgu. Jeśli skończą im się torpedy albo są zbyt poobijane,
  wycofują się. Jeśli uciec nie mogą, bo są wolniejsze, walczą do końca.
- Każdy dywizjon AI, który nie jest w stanie uciec przed pościgiem,
  przestaje uciekać i odwraca się do walki. Koniec z bezkarnym gonieniem
  kulejących przeddrednotów.
- Uszkodzone, wolne okręty są odłączane od dywizjonu i wycofują się
  osobno, więc sprawne jednostki nie wloką się za nimi.
- Działa AI skupiają ogień na już uszkodzonych celach.
- Każda nacja walczy według własnej doktryny historycznej, zapisanej w
  Mods\battleDoctrines.csv: Royal Navy trzyma dystans i linię, Japonia
  szarżuje torpedami z 16 km i akceptuje straty, Włosi walczą tylko przy
  przewadze, Niemcy używają flotylli torpedowych do osłony odwrotu.
  Doktryny oparte na Grand Fleet Battle Orders, War Instructions USN i
  literaturze o Kaisen Yōmurei; pełne zestawienie w
  DOKTRYNY_TAKTYCZNE_NACJI.md.

KAMPANIA, W KTÓREJ AI SIĘ NIE PODDAJE
- Nacje AI, których nie stać na pancernik, budują krążowniki zamiast
  stać z pustą stocznią.
- Gdy twoja flota przerasta najsilniejszego rywala o połowę, nacje AI
  dostają nadzwyczajne środki na marynarkę. Dominacja ma cenę.
- Strażnik budowy: jeśli gra z jakiegoś powodu nie przeliczy budowy
  okrętów nacji AI w danej turze, dodatek robi to sam.
- Twoje projekty z kampanii trafiają automatycznie do puli projektów
  współdzielonych, więc AI może budować według nich.

ŚWIAT, KTÓRY ŻYJE
- Suwerenne państwa minor (Ameryka Łacińska, Persja, Syjam, Bałkany,
  Skandynawia i inne) odzyskują swoje terytoria. W oryginalnym DIP
  większość z nich istniała tylko na papierze; teraz zawierają sojusze,
  kupują okręty i biorą udział w wojnach jak w vanilla.
- Minory mają silniejszą obronę prowincji, podwojoną pojemność portów w
  handlu okrętami i częściej zamawiają u ciebie jednostki.
- Dyplomacja ma konsekwencje: poprawa stosunków jest 1,5x silniejsza,
  pogorszenie 4x silniejsze, zarówno dla gracza, jak i dla AI.
- Kryzys po przegranej wojnie jest dotkliwy, ale nie wpędza państw w
  spiralę bez wyjścia (złagodzone kary budżetowe, inflacja wojenna,
  krótszy minimalny czas do pokoju z powodu załamania gospodarki).

WIERNOŚĆ HISTORYCZNA I SWOBODA PROJEKTOWANIA PANCERNIKÓW
- Przebijalność z fizyki, nie z suwaka. W DIP przebijalność rosła mniej
  więcej liniowo z kalibrem (ok. +58 mm na każdy cal). Iron Admiral
  przelicza krzywe dla 21 kalibrów (1"-21") i 5 generacji dział wzorem
  De Marre'a (T/D = K x D^1,071 x (W/D^3)^0,714 x V^1,429), skalibrowanym
  na sześciu udokumentowanych tabelach przebijalności z navweaps.com:
  US 8"/55, niemieckie 28 cm SK C/34, US 12"/50, brytyjskie 13,5"/45
  i 15"/42, US 16"/50 Mk 7. Błąd względem realnych tabel: 0,5-13%.
  Każdy kaliber i generacja opiera się na masie pocisku i prędkości
  wylotowej prawdziwego działa (lista: gun_sources_reference.csv).
  Efekt: małe kaliby nie drapią pasa pancernego, 13"+ są silne, ale nie
  liniowo absurdalne, generacje poprawiają się monotonicznie.
- Wagi dział według prawdziwego metalu. Waga lufy to prawo potęgowe
  W = 0,05564 x D^2,7732 t, skalibrowane na 16"/50 Mk 7, 15"/42 Mk I
  i 46 cm Type 94 z Yamato (błąd ekstrapolacji ok. 4%). Waga struktury
  wieży została przy wartościach DIP, bo próba prawa potęgowego
  zerowała małe wieże i uniemożliwiała ich opancerzenie.
- Traktaty morskie, po raz pierwszy w modzie. DIP nie miał żadnego
  limitu tonażu pancerników. Technologia kadłuba z 1919 r. wprowadza
  limit Traktatu Waszyngtońskiego (35 000 t, świat 5:5:3), rok 1936
  klauzulę eskalacyjną II Traktatu Londyńskiego (45 000 t, historycznie
  uruchomioną, gdy Japonia odmówiła podpisu), a w 1940 r. traktaty
  wygasają. Epoka traktatowa jest odczuwalna jako ograniczenie projektu
  dokładnie wtedy, kiedy odczuwały ją prawdziwe admiralicje.
- Po traktatach: swoboda do 240 000 t. Gdy limity padają, warstwa Super
  Battleship nie kończy się na 78 500-130 000 t jak w DIP. Każdy z 14
  kadłubów superpancerników jest proporcjonalnie rozciągnięty do pasma
  200 000-240 000 t (bb_8_china 200 000 t ... bb_6_iowa 224 000 t ...
  bb_7_bismarck 240 000 t), a globalny sufit BB to twarde 240 000 t
  zamiast nieskończoności. Liczba jest przemyślana: stal kadłuba nigdy
  nie była granicą (Seawise Giant, 564 763 DWT), granicą były suche
  doki; niemieckie studium H-44 (ok. 131 000 t) uznano za niemożliwe do
  zbudowania na pochylniach lat 40. Pasmo 200 000-240 000 t oznacza
  "lepszą infrastrukturę niż mocarstwa lat 40., ale nie dzisiejsze
  700-metrowe doki". Okręt, którym H-44 nie mógł być, jest tu do
  zbudowania, także przez AI.
- Kadłuby, które dało się zbadać, ale nie zbudować: naprawione. W DIP
  29 wariantów "Modern Battleship" (Iowa, North Carolina, Bismarck,
  Richelieu, King George V, Littorio, Sojuz...) odblokowywało się
  z minimalnym tonażem 42 500-66 000 t przy limicie traktatowym
  35 000-45 000 t, więc technologia była martwa. Ich minimum to teraz
  35 000 t, bliżej historii niż liczby DIP (South Dakota ok. 37 000 t,
  KGV ok. 36 000 t, Bismarck ok. 41 700 t, Littorio ok. 40 700 t). Ten
  sam audyt naprawił 23 wcześniejsze kadłuby BB, całą warstwę Super
  Battleship w chwili odblokowania, 5 trwale niebudowalnych kadłubów
  "Hybrid Destroyer" (ostatnia technologia DD ustawiała limit 4 200 t
  i w tym samym wierszu odblokowywała kadłuby 4 450-4 750 t) oraz
  uszkodzony wiersz CSV, który po cichu wycinał listę odblokowań
  wszystkich nowoczesnych pancerników.
- Long Lance. Technologia torped tlenowych daje +100% zasięgu zamiast
  +40%: Typ 93 osiągał 40 000 m przy 33-35 w., około trzy razy więcej
  niż jakakolwiek współczesna mu torpeda (US Mk 15: 13 700 m, UK Mk IX:
  12 300 m, niemiecka G7a: 12 000 m). W pełni rozwinięte torpedy sięgają
  teraz ok. 28 000-35 000 m.
- Radar, który coś znaczy. Statystyka "widoczność nocna" istniała w DIP,
  ale żadna technologia jej nie nadawała. Trzy technologie radarowe
  (1935/1940/1945) dają teraz widoczność nocną +20/35/50 i celność
  w złej pogodzie +15/20/25. Nocne starcia II wojny stają się możliwe.

OKRĘTY I TECHNOLOGIA (SKRÓT)
- Przebijalność pancerza przeliczona od podstaw dla 21 kalibrów i 5
  generacji dział według fizyki penetracji (wzór De Marre'a), zamiast
  liniowego wzrostu z kalibrem.
- Wagi luf przeliczone na prawa potęgowe skalibrowane na rzeczywistych
  działach (16"/50 Mk7, 15"/42 Mk I, 46 cm Type 94).
- Traktaty morskie, których DIP w ogóle nie modelował (pancerniki nie
  miały żadnego limitu tonażu): Traktat Waszyngtoński 35 000 t od
  technologii wytrzymałości kadłuba z 1919 r., klauzula eskalacyjna
  II Traktatu Londyńskiego 45 000 t od 1936 r., wygaśnięcie traktatów
  w 1940 r.
- Podniesiony tonaż pancerników po 1940 r.: warstwa Super Battleship
  z 78 500-130 000 t w DIP do 200 000-240 000 t na kadłub (bb_8_china
  200 000 t ... bb_7_bismarck 240 000 t), z twardym sufitem 240 000 t
  zamiast nieskończoności. Kadłuby Modern Battleship od 35 000 t;
  limit niszczycieli ustawiony na realistyczne 4 200 t.
- Naprawione kadłuby, których w DIP nie dało się zbudować przez błędne
  limity tonażu: Modern Battleship (29 wariantów), Super Battleship,
  wcześniejsze BB oraz 5 kadłubów niszczycieli.
- Naprawiony uszkodzony wiersz CSV blokujący odblokowanie nowoczesnych
  pancerników.
- Radar wreszcie coś daje w nocy i przy złej pogodzie (aim_rangefinder
  11/12/13: widoczność nocna +20/35/50, odporność na pogodę).
- Torpedy tlenowe "Long Lance": +100% zasięgu zamiast +40%.
- Torpedy w automatycznym ogniu odpalane agresywniej i z większej
  odległości (dotyczy wszystkich okrętów).

BADANIA I CHARAKTER NACJI
- Badania szybsze o ok. 20% względem poprzedniej kalibracji, koszt
  technologii x10, premia dla lidera wydatków na badania zmniejszona do
  rozsądnego poziomu.
- Specjalizacje technologiczne nacji wzmocnione x1,5: Japonia w
  torpedach i lekkich krążownikach, Niemcy w pancerzu i optyce, USA w
  kadłubach i artylerii itd. Nacje różnią się od siebie naprawdę.
- Nowy profil Cesarstwa Bizantyjskiego (Grecja w DIP): ciężki pancerz,
  najcięższe kalibry, szybkie krążowniki ciężkie i lekkie.
- Bunty i niestabilność wewnętrzna częstsze, silniejsze i zróżnicowane
  geograficznie.

STABILNOŚĆ
- Dodatek przechwytuje dwa błędy w kodzie samej gry
  (CheckMinorNationThreat, TryGenerateSmallerNationsBattle: "An item
  with the same key has already been added"), które w DIP przerywały
  przetwarzanie tury. Tura kończy się normalnie, błąd trafia do logu.
- Państwo AI z zerowym budżetem nie znika z mapy w pierwszej turze:
  rozpad gospodarczy następuje dopiero po 24 kolejnych miesiącach
  kryzysu (utrata wszystkich portów nadal rozwiązuje od razu).
- Osłona ładowania okrętów: projekt zapisany bez listy kalibrów wież
  nie wysypuje już ładowania bitwy (błąd TweaksAndFixes 3.16.7,
  naprawiony też w 3.21.1).

Wszystkie zachowania dodatku mają przełączniki i wartości w
UserData\MelonPreferences.cfg, sekcja [DIP_GG_Fixes]; doktryny narodowe
w Mods\battleDoctrines.csv. Nic nie jest wymuszone.

--------------------------------------------------------------------------
STAN TESTÓW
--------------------------------------------------------------------------
Potwierdzone w kampanii testowej (log MelonLoadera): przechwytywanie
błędów tury, wydarzenia równowagi sił, eksport projektów współdzielonych,
szarże torpedowe, walka zamiast ucieczki, odłączanie wolnych okrętów,
doktryny narodowe. Nieprzetestowane w dłuższej grze: kierunek działania
mnożnika dystansu w doktrynach (distanceMult) oraz zmiany gospodarcze
na przestrzeni całej kampanii. Pełna historia zmian z uzasadnieniami,
źródłami i statusem testów: CHANGELOG_PL.txt (33 sekcje).

--------------------------------------------------------------------------
INSTALACJA
--------------------------------------------------------------------------
Sama gra nie czyta folderu Mods. Dane CSV wczytuje TweaksAndFixes,
który jest modem pod MelonLoadera, dlatego instalator sam przygotowuje
MelonLoadera.

Rozpakuj ZIP w dowolne miejsce i kliknij dwukrotnie Install.bat.
Uruchamia on installer\Install.ps1, jawny skrypt PowerShell, który można
najpierw przeczytać. Bez exe. Skrypt:
- znajduje grę przez Steam (albo pyta o folder),
- przenosi obecny folder Mods (i MelonLoader, jeśli to nie 0.6.6) do
  <folder gry>\Iron_Admiral_backup\<data>\,
- pobiera oficjalne MelonLoader.x64.zip v0.6.6 z wydania LavaGang na
  GitHubie (tylko gdy MelonLoader 0.6.6 nie jest już zainstalowany),
  sprawdza sumę SHA-256 (687b8260...3c0db) i przerywa przy niezgodności,
- instaluje MelonLoadera i pliki moda.
Internet jest potrzebny w trakcie instalacji (pobranie MelonLoadera)
i przy pierwszym uruchomieniu. Przed instalacją zamknij grę.

PIERWSZE URUCHOMIENIE
Pierwszy start po instalacji MelonLoadera trwa kilka minut i wymaga
internetu - MelonLoader generuje pliki pomocnicze gry. Obok gry pojawia
się okno konsoli; to normalne.

ZAPISY GRY
Zacznij nową kampanię. Właściciele prowincji i inne dane startowe
działają tylko w nowych kampaniach. Starsze zapisy mogą się wczytać,
ale nie są wspierane.

ODINSTALOWANIE
Uruchom Uninstall.bat z <folder gry>\Iron_Admiral_backup\.
Przywraca poprzedni folder Mods oraz, jeśli instalator go podmienił,
poprzedni MelonLoader (albo usuwa MelonLoader, jeśli go wcześniej nie
było).

--------------------------------------------------------------------------
USTAWIENIA DODATKU (UserData\MelonPreferences.cfg, [DIP_GG_Fixes])
--------------------------------------------------------------------------
AutoShareDesigns / ShareToAllNations      projekty współdzielone
CruisersWhenPoor, PoorBigShipRatioFactor,
  PoorCruiserRatioFactor                  krążowniki zamiast BB
MinorPortCapacityMultiplier               pojemność portów minorów
BalanceOfPowerEvents, BalanceTonnageRatio,
  BalanceEventChance, BalanceGrantMonths  równowaga sił
DiplomacyAttitudeMultiplier (1,5),
  DiplomacyNegativeAttitudeMultiplier (4) dyplomacja
FixStalledConstruction                    strażnik budowy
EconomicCollapseGraceMonths (24)          karencja przed rozpadem
LightShipTorpedoCharge, TorpedoChargeStartRange,
  TorpedoChargeRangeMargin,
  LightShipChargeAbortDamage,
  TorpedoRangeFallback                    szarża torpedowa
FightWhenCannotEscape, PursuitRange,
  EscapeSpeedMargin                       walka zamiast ucieczki
DetachSlowShips, SlowShipSpeedRatio,
  DetachCooldownSeconds                   odłączanie wolnych okrętów
FocusFire, FocusFireMinDamage             skupianie ognia
BattleAiThinkSeconds, BattleAiLog         częstość decyzji, log
Mods\battleDoctrines.csv                  doktryny per nacja

--------------------------------------------------------------------------
ZNANE PROBLEMY
--------------------------------------------------------------------------
- W logu nadal pojawia się "An item with the same key has already been
  added. Key: <nazwa> (Province)" - to błąd kodu gry, przechwytywany
  przez dodatek; tura kończy się normalnie.
- Komunikaty "failed to generate ship ... in 4 tries", "tonnage ... is
  out of bounds, clamped", "non-unique name in PartData" i "missing
  hull: ..._mission" pochodzą z danych DIP i są nieszkodliwe.

--------------------------------------------------------------------------
AUTORZY I LICENCJE (folder licenses)
--------------------------------------------------------------------------
- Dreadnought Improvement Project (DIP) "The Great Game" - Brother Munro
  i zespół DIP. Licencja Apache 2.0. Ta paczka modyfikuje pliki DIP;
  zmiany są opisane w CHANGELOG_PL.txt.
- TweaksAndFixes - NathanKell. Licencja MIT.
- MelonLoader - LavaGang. Licencja Apache 2.0.
- Iron Admiral / DIP GG Extended (zmiany danych, dodatek DIP_GG_Fixes,
  instalator) -
  Klaudiusz Ledwoch (Steam: Klod76). Kod źródłowy w folderze source,
  licencja Apache 2.0.
- Ultimate Admiral: Dreadnoughts - Game-Labs. To nieoficjalna
  modyfikacja fanowska; problemów zmodowanej gry nie zgłaszaj Game-Labs.
