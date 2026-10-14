# Doktryny taktyczne nacji (1890–1940) → parametry AI bitewnego

Status: propozycja (asystenta) do dyskusji, NIE wdrożona. Źródła historyczne podane w kolumnie "Doktryna";
tam, gdzie brak regulaminu źródłowego, opis pochodzi z literatury (oznaczone *lit.*).
Parametry w ostatnich kolumnach odnoszą się do DIP_GG_Fixes (sekcja [DIP_GG_Fixes] w MelonPreferences.cfg);
obecnie są globalne, wersja per nacja wymaga rozszerzenia dodatku.

| Nacja | Doktryna (źródło) | Dystans walki | Odwrót / ryzyko | Lekkie okręty i torpedy | Noc | Propozycja parametrów |
|---|---|---|---|---|---|---|
| Wielka Brytania | Grand Fleet Battle Orders (Jellicoe 1914–16, tekst w "The Grand Fleet"): jedna linia bojowa, ogień skoncentrowany z dużego dystansu, odwrót od salwy torpedowej ("turn away"), niszczyciele jako osłona przeciwtorpedowa, nie szarża | daleki, otwarcie z maks. zasięgu | ostrożny: nie ryzykuje floty, utrzymuje przewagę | DD bronią linii, atak torpedowy tylko na rozkaz | unika walki nocnej (1916) | dystans ×1,2; ChargeStartRange 8000; AbortDamage 0,45; EscapeMargin 1,0; focus fire TAK |
| Niemcy | Hochseeflotte, Tirpitz/Scheer (*lit.*: Herwig, "Tirpitz Technique" USNI 1939): bitwa na własnych warunkach blisko baz, Gefechtskehrtwendung (zwrot całej linii o 180° pod osłoną torped), masowe ataki torpedowe flotylli dla przykrycia odwrotu | średni, dobre celowanie na 10–15 km | ucieka przy niekorzystnym bilansie, ale dopiero po oddaniu salwy torpedowej | flotylle TB/DD szarżują grupowo, by przykryć odwrót linii | tak, trening nocny (Jutlandia) | dystans ×1,0; ChargeStartRange 14000; AbortDamage 0,7 (szarża jako osłona); EscapeMargin 1,05 |
| USA | War Instructions USN 1934/1944 (FTP 143, HyperWar): "Sound Military Decision", linia bojowa + koncentracja ognia, przewaga materiałowa, metodyczne zbliżanie | daleki do średniego | umiarkowany; walczy przy przewadze, inaczej odrywa się w porządku | DD atakują po rozbiciu osłony wroga, nie na początku | neutralny (do 1941 słabo) | dystans ×1,1; ChargeStartRange 10000; AbortDamage 0,5; EscapeMargin 1,0 |
| Japonia | Kaisen Yōmurei 1910/1934 (*lit.*: Japanese Monographs, Evans & Peattie "Kaigun"): bitwa rozstrzygająca, "outrange" (ogień z poza zasięgu wroga), nocny atak torpedowy krążowników i niszczycieli przed bitwą główną, akceptacja strat | daleki dla BB, bardzo bliski dla CL/DD | agresywny, nie odpuszcza; odwrót dopiero przy stratach katastrofalnych | CL/DD/TB szarżują z daleka i bez względu na straty; torpedy jako broń główna | tak, doktryna nocna | dystans ×1,2 (BB); ChargeStartRange 16000; AbortDamage 0,8; EscapeMargin 1,1; torpedy agresywne |
| Francja | *lit.*: Jeune École (1880–1900: TB i krążowniki vs linia), potem Marine nationale lat 20.–30.: szybkie ciężkie krążowniki i kontrtorpedowce, unikanie walki generalnej bez przewagi | średni/bliski dla kontrtorpedowców | unika bitwy generalnej; wycofuje się, gdy nie ma przewagi | duże DD (contre-torpilleurs) atakują samodzielnie, CL osłaniają | neutralny | dystans ×0,95; ChargeStartRange 12000; AbortDamage 0,6; EscapeMargin 1,0 |
| Rosja | *lit.*: po Cuszimie "ogień skoncentrowany eskadry", obrona pozycji minowo-artyleryjskiej (Bałtyk), flota jako wsparcie wybrzeża; mało inicjatywy w bitwie otwartej | średni | defensywny; nie ściga, walczy przy brzegu | TB/DD do obrony pozycji minowej, nie do szarży | słabo | dystans ×1,0; ChargeStartRange 9000; AbortDamage 0,5; EscapeMargin 0,95 |
| Włochy | *lit.*: Regia Marina – "szybkość ponad pancerz", walka tylko przy przewadze, odrywanie się dzięki prędkości, duży nacisk na dystans i unikanie torped | daleki, strzelanie na maks. dystans | bardzo ostrożny; ucieka, gdy jest szybszy | CL/DD jako zwiad i osłona, szarża rzadko | unika | dystans ×1,25; ChargeStartRange 7000; AbortDamage 0,4; EscapeMargin 1,0 (ucieka, jeśli szybszy) |
| Austro-Węgry | *lit.*: flota obronna na Adriatyku (Haus, Horthy): "fleet in being", wypady szybkimi jednostkami, bitwa tylko przy przewadze lokalnej | średni | defensywny; chroni ciężkie okręty | szybkie CL i DD do wypadów (Otranto 1917) | tak (wypady nocne) | dystans ×1,0; ChargeStartRange 11000; AbortDamage 0,55; EscapeMargin 1,0 |
| Hiszpania | *lit.*: brak własnej doktryny ofensywnej, wzorce brytyjskie; flota wsparcia i obrony kolonii | średni | defensywny | ograniczone | neutralny | dystans ×1,0; ChargeStartRange 9000; AbortDamage 0,5; EscapeMargin 1,0 |
| Chiny | *lit.*: Beiyang 1894 – linia abeam, słabe wyszkolenie, nacisk na przybrzeżną obronę | bliski/średni | nierówny; łatwo się załamuje | słabe | neutralny | dystans ×0,9; ChargeStartRange 8000; AbortDamage 0,4; EscapeMargin 0,95 |
| Portugalia, Holandia, Skandynawia | *lit.*: małe floty obronne; Holandia – obrona Indii Wschodnich lekkimi krążownikami i DD, Skandynawia – obrona wybrzeża, pancerniki obrony wybrzeża | średni/przybrzeżny | defensywny, unika straty jednostek | CL/DD jako główna siła, atak torpedowy tylko lokalnie | neutralny | dystans ×1,0; ChargeStartRange 10000; AbortDamage 0,5; EscapeMargin 1,0 |
| Grecja / Bizancjum (DIP) | nacja fikcyjna w DIP; profil ustalony w tej kampanii: suma greece_ai i russia_ai (ciężki pancerz, duże kalibry, ciężkie i lekkie szybkie krążowniki) | daleki (duże kalibry) | agresywny przy przewadze pancerza | szybkie CL do szarży torpedowej | neutralny | dystans ×1,15; ChargeStartRange 13000; AbortDamage 0,65; EscapeMargin 1,05 |

## Znaczenie parametrów
- dystans ×N — mnożnik preferowanego dystansu walki (dziś globalny ai_distance_mod w params.csv; per nacja wymagałby łatki na RangeCalc).
- ChargeStartRange — odległość do wrogiego BB/BC/CA, od której lekkie okręty AI zaczynają szarżę torpedową.
- AbortDamage — próg średnich uszkodzeń dywizjonu, po którym szarża jest przerywana.
- EscapeMargin — mnożnik prędkości pościgu; >1 oznacza, że nacja walczy nawet będąc nieco szybsza (nie ucieka).
- focus fire — skupianie ognia na uszkodzonych celach (obecnie globalnie TAK).

## Źródła
- Jellicoe, "The Grand Fleet 1914-1916" (zawiera Grand Fleet Battle Orders) – https://www.gutenberg.org/cache/epub/75323/pg75323.txt
- "War Instructions, United States Navy" 1934/1944 – https://ibiblio.org/hyperwar/USN/ref/WarInst/index.html
- Japanese Monographs (US Army, powojenne) – https://www.ibiblio.org/hyperwar/Japan/Monos/index.html
- "Tirpitz Technique", USNI Proceedings 1939 – https://www.usni.org/magazines/proceedings/1939/september/tirpitz-technique
- Herwig, "Luxury Fleet: The Imperial German Navy 1888-1918"; Evans & Peattie, "Kaigun" (literatura, bez linku)
