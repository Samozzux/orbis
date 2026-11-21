using System;
using System.Collections.Generic;
using Orbis.Core.World.Fb;

namespace Orbis.Core.Population
{
    public enum Sex : byte { Male = 0, Female = 1 }

    /// <summary>Titolo di studio: nessuno, licenza elementare, licenza media, diploma, laurea.</summary>
    public enum Education : byte { None = 0, Primary = 1, Lower = 2, Upper = 3, Degree = 4 }

    public enum Occupation : byte
    {
        Toddler = 0,
        Pupil,
        UniversityStudent,
        Employee,
        SelfEmployed,
        Entrepreneur,
        Unemployed,
        Homemaker,
        Retired,
    }

    /// <summary>Posizione in famiglia.</summary>
    public enum FamilyRole : byte { Single = 0, Partner, Child, Parent, Relative }

    /// <summary>Tipo di famiglia (stesse categorie ISTAT).</summary>
    public enum HouseholdType : byte { Alone = 0, Couple, CoupleWithChildren, SingleParent, Other }

    public enum Tenure : byte { Owner = 0, Renter }

    /// <summary>Che cosa sta facendo un cittadino (giornata, docs/03 §3).</summary>
    public enum CitizenActivity : byte { Home = 0, Sleep, Eat, Work, School, Shopping, Leisure }

    /// <summary>Come si sposta nell'ultimo viaggio.</summary>
    public enum TravelMode : byte { Walk = 0, Car, Transit }

    /// <summary>Dove si trova: casa, lavoro, scuola, negozio o svago (indice nei rispettivi elenchi), fuori città.</summary>
    public enum LocationKind : byte { Home = 0, Work, School, Shop, Leisure, Outside }

    /// <summary>Stato del record: i cittadini morti o partiti restano nell'archivio (gli indici non cambiano).</summary>
    [Flags]
    public enum CitizenFlags : byte { None = 0, Dead = 1, Gone = 2 }

    /// <summary>
    /// Cittadino: un record compatto (docs/01 §6: ogni cittadino esiste sempre come record persistente).
    /// I riferimenti sono indici: famiglia in <see cref="CityPopulation.Households"/>, lavoro in
    /// <see cref="CityPopulation.Workplaces"/> (<see cref="WorkOutside"/> = fuori comune), scuola in
    /// <see cref="CityPopulation.Schools"/>.
    /// </summary>
    public struct Citizen
    {
        public const int None = -1;
        public const int WorkOutside = -2;

        public int Household;
        public int Partner;
        public int Work;
        public int School;
        /// <summary>Giorno di nascita: giorni dall'1/1/1900.</summary>
        public int BirthDay;
        /// <summary>Reddito lordo annuo in euro (0 = nessun reddito).</summary>
        public int Income;
        /// <summary>Risparmi in euro.</summary>
        public int Savings;
        public ushort FirstName;
        public ushort Surname;
        public Sex Sex;
        public Education Education;
        public Occupation Occupation;
        public FamilyRole Role;
        /// <summary>Tratti di personalità: un bit per chiave di <see cref="PopulationBalance.TraitKeys"/>.</summary>
        public ulong Traits;
        /// <summary>Aspetto: carnagione, capelli, altezza, corporatura… impacchettati (il personaggio si ricostruisce identico).</summary>
        public uint Appearance;
        /// <summary>Gusti: cibo, musica, sport, svago (4 bit ciascuno).</summary>
        public uint Tastes;
        // bisogni 0–100 (docs/03 §2)
        public byte Hunger, Energy, Hygiene, Fun, Social, Comfort, Health;
        public byte Mood;
        /// <summary>Felicità 0–100 (docs/03 §6), aggiornata ogni giorno; 0 = non ancora calcolata.</summary>
        public byte Happiness;
        /// <summary>Minuti dell'ultimo viaggio verso il lavoro o la scuola (0 = nessuno).</summary>
        public byte Commute;
        /// <summary>Preoccupazione: il fattore della felicità che va peggio (<see cref="HappinessFactor"/>; Count = nessuna).</summary>
        public byte Worry;
        public CitizenFlags Flags;

        // giornata (livello L1): attività, luogo e viaggio; i minuti si contano dall'1/1/1900
        public CitizenActivity Activity;
        public LocationKind Location, PreviousLocation;
        /// <summary>Indice del luogo (lavoro, scuola, negozio o svago); per casa e fuori città non serve.</summary>
        public int LocationIndex, PreviousIndex;
        /// <summary>Fine dell'attività, partenza e arrivo dell'ultimo viaggio (minuti): tra i due è per strada.</summary>
        public int ActivityEnd, TravelStart, TravelEnd;
        public TravelMode Mode;
        /// <summary>Linea del trasporto pubblico dell'ultimo viaggio (-1 se a piedi o in auto).</summary>
        public int TransitLine;

        /// <summary>Vive in città (non è morto né partito).</summary>
        public bool Alive => Flags == CitizenFlags.None;

        public static readonly DateTime Epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public DateTime BirthDate => Epoch.AddDays(BirthDay);

        public int AgeOn(DateTime date)
        {
            var b = BirthDate;
            int age = date.Year - b.Year;
            if (date.Month < b.Month || (date.Month == b.Month && date.Day < b.Day)) age--;
            return age;
        }

        public bool HasTrait(int index) => (Traits & (1UL << index)) != 0;

        public bool Works => Occupation == Occupation.Employee || Occupation == Occupation.SelfEmployed || Occupation == Occupation.Entrepreneur;
    }

    /// <summary>Famiglia: chi ci vive e dove (casa = indice in <see cref="CityPopulation.Dwellings"/>, appartamento).</summary>
    public sealed class Household
    {
        public HouseholdType Type;
        public int Dwelling = -1;
        public int Unit;
        public Tenure Tenure;
        /// <summary>Affitto in euro al mese (0 se la casa è di proprietà).</summary>
        public int Rent;
        /// <summary>Porzioni di cibo nel frigo e in dispensa.</summary>
        public int Fridge;
        /// <summary>Spesa alimentare di oggi (già pagata ai negozi): la vita del giorno la toglie dalla spesa generica.</summary>
        public int FoodSpentToday;
        /// <summary>Auto della famiglia.</summary>
        public byte Cars;
        /// <summary>Biglietti del trasporto pubblico di oggi in centesimi: la vita del giorno li fa pagare.</summary>
        public int TransitCents;
        /// <summary>Parte di <see cref="TransitCents"/> spesa sulle linee del comune: entra nella sua cassa.</summary>
        public int CityTransitCents;
        public readonly List<int> Members = new List<int>();
    }

    /// <summary>Edificio abitato del mondo reale (o del catalogo), con la superficie dei piani.</summary>
    public sealed class Dwelling
    {
        public long OsmId;
        public int TileX, TileY;
        /// <summary>Posizione in metri nel sistema della regione (WorldFrame).</summary>
        public float X, Y;
        public float FloorArea;
        public int Units;
        /// <summary>Abitanti che ci stanno (stima dalla superficie).</summary>
        public int Capacity;
        public BuildingUse Use;
        public string Address;

        public Dwelling Clone() => (Dwelling)MemberwiseClone();
    }

    public enum PlaceLevel : byte { Work = 0, Kindergarten, School, University }

    /// <summary>Posto di lavoro o di studio: edificio non residenziale o attività (POI) dentro un edificio.</summary>
    public sealed class Place
    {
        public long OsmId;
        public int TileX, TileY;
        public float X, Y;
        public PlaceLevel Level;
        /// <summary>"Hospital", "shop/supermarket", "amenity/bar"…</summary>
        public string Category;
        public string Name;
        /// <summary>Posti (lavoratori o studenti).</summary>
        public int Capacity;
        public Education MinEducation;

        // conto dell'attività (docs/03 §4 "attività commerciali con conto economico reale"); i posti pubblici non ce l'hanno
        /// <summary>Datore di lavoro pubblico (ospedale, scuola, comune…): stipendi pagati dallo Stato.</summary>
        public bool Public;
        public bool Closed;
        public long Cash;
        /// <summary>Incassi e costi medi al giorno (media mobile sull'ultimo mese circa).</summary>
        public long RevenueDay, CostsDay;
        /// <summary>Mesi di fila con la cassa sotto la soglia: dopo alcuni l'attività chiude.</summary>
        public byte BadMonths;
        /// <summary>Scorte (porzioni) dei negozi di alimentari.</summary>
        public int Stock;
        /// <summary>Vendite di oggi ai cittadini (già in cassa): a fine giornata entrano negli incassi e nelle tasse.</summary>
        public long SalesToday;

        public Place Clone() => (Place)MemberwiseClone();
    }

    /// <summary>Tutti i cittadini di un comune con famiglie, case, posti di lavoro e scuole.</summary>
    public sealed class CityPopulation
    {
        public string MunicipalityCode;
        public string MunicipalityName;
        public int Seed;
        public DateTime Date;
        public Citizen[] Citizens = Array.Empty<Citizen>();
        public int Count;
        public readonly List<Household> Households = new List<Household>();
        public readonly List<Dwelling> Dwellings = new List<Dwelling>();
        public readonly List<Place> Workplaces = new List<Place>();
        public readonly List<Place> Schools = new List<Place>();
        /// <summary>Tabella dei nomi (Citizen.FirstName) e dei cognomi (Citizen.Surname).</summary>
        public readonly List<string> FirstNames = new List<string>();
        public readonly List<string> Surnames = new List<string>();
        public string[] TraitKeys = Array.Empty<string>();

        // contatori della vita della città (dal giorno della generazione)
        /// <summary>Denaro entrato da fuori (stipendi dello Stato, pensioni, clienti di fuori…) e uscito (tasse allo Stato, affitti, merci…).</summary>
        public long ExternalIn, ExternalOut;
        public int Births, Deaths, Arrived, Left;
        /// <summary>Aiuti sociali pagati dal comune nell'ultima giornata.</summary>
        public long LastWelfare;
        /// <summary>Biglietti delle linee del comune incassati nell'ultima giornata (euro).</summary>
        public long LastCityFares;
        /// <summary>Felicità media dei cittadini all'ultimo calcolo (0 = mai calcolata).</summary>
        public float AverageHappiness;
        /// <summary>Cittadini scontenti all'ultimo calcolo.</summary>
        public int UnhappyCount;
        /// <summary>Media di ciascun fattore della felicità (<see cref="HappinessFactor"/>).</summary>
        public readonly float[] HappinessFactors = new float[(int)HappinessFactor.Count];

        // reazioni (blocco 5b)
        /// <summary>Gradimento del sindaco: quota di adulti non scontenti (0–100).</summary>
        public float Approval;
        /// <summary>Ultime reazioni (commenti, petizioni, proteste, scioperi) per le notizie.</summary>
        public readonly List<Reaction> Feed = new List<Reaction>();
        public readonly List<Petition> Petitions = new List<Petition>();
        /// <summary>Giorno dell'ultima protesta in piazza e protesta in arrivo (fattore + 1, 0 = nessuna).</summary>
        public int LastProtestDay;
        public byte ProtestPending;
        /// <summary>Giorni di fila con il lavoro che preoccupa; giorno dello sciopero (0 = nessuno).</summary>
        public int LowWorkDays, StrikeDay;
        /// <summary>Ultimo giorno simulato (giorni dall'1/1/1900), per non ripetere una giornata.</summary>
        public int LastDay;

        public int AliveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Count; i++)
                    if (Citizens[i].Alive) n++;
                return n;
            }
        }

        public ref Citizen this[int i] => ref Citizens[i];

        public int Add(in Citizen c)
        {
            if (Count == Citizens.Length) Array.Resize(ref Citizens, Math.Max(64, Count * 2));
            Citizens[Count] = c;
            return Count++;
        }

        public string FullName(int i) => FirstNames[Citizens[i].FirstName] + " " + Surnames[Citizens[i].Surname];
    }
}
