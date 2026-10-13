using System;
using System.Collections.Generic;
using System.Linq;

namespace BurgerEmpire.Core
{
    [Serializable] public class Progress : RestaurantProgress
    {
        public const int CurrentVersion=15;
        public int version=CurrentVersion,coins=120,gems=0,xp=0;
        public int activeBranch;
        public bool londonOpen;
        public RestaurantProgress istanbul,london;
        public int dailyMenuMask;
        public bool dailyVarietyClaimed;
        public string day="";
        public int dailyServed,dailyCleaned,dailyEarned;
        public bool dailyServeClaimed,dailyCleanClaimed,dailyEarnClaimed;
        public int dailyReviews;
        public bool dailyReviewClaimed;
        public List<string> achievementsUnlocked=new List<string>(),achievementsClaimed=new List<string>();
    }
    [Serializable] public class Table
    {
        public int id, customer;
        public float x, z;
        public bool dirty, reserved;
    }
    [Serializable] public class Customer
    {
        public int id, kind, table = -1, price;
        public string name, phase = "arriving", order = "none", meal;
        public string product="classic", station="grill";
        public float x = 10.7f, z = 8.5f, patience, maxPatience, timer, cookTime;
        public float readySeconds,paymentSeconds;
        public bool reserved,drive,delivery;
        public int deliveryBay=-1;
        public List<Point> path = new List<Point>();
        public bool pathBlocked;
        [NonSerialized] public CrowdMotion crowd=new CrowdMotion();
    }
    [Serializable] public class Actor
    {
        public string role, job = "", label;
        public int customer, table = -1, stage;
        public float x, z, timer;
        public List<Point> path = new List<Point>();
        [NonSerialized] public CrowdMotion crowd=new CrowdMotion();
    }
    [Serializable] public class Point { public float x, z; public Point() {} public Point(float a, float b) { x = a; z = b; } }
    [Serializable] public class Event
    {
        public int id; public string text, kind; public float x, z;
    }
    [Serializable] public class Offer
    {
        public string id, name, description, icon;
        public string lockedReason="";
        public int cost, level, max;
    }
    [Serializable] public class Mission
    {
        public string id, title; public int current, target, reward; public bool claimed;
    }
    [Serializable] public class Snapshot
    {
        public Progress progress;
        public List<Customer> customers; public List<Actor> actors; public List<Table> tables;
        public List<Event> events; public List<Offer> offers; public List<Mission> missions;
        public float time, rating, cleanliness; public bool paused, rush;
        public string hint, suggestedAction, playerJob;
        public int level, queue, ready;
        public int tutorialStep;
        public string tutorialTitle;
    }

    // Engine-independent, authoritative simulation shared by Unity and the local preview.
    public partial class Simulation
    {
        public Progress P { get; private set; }
        public readonly List<Customer> Customers = new List<Customer>();
        public readonly List<Actor> Actors = new List<Actor>();
        public readonly List<Table> Tables = new List<Table>();
        public readonly List<Event> Events = new List<Event>();
        public float Time { get; private set; }
        public bool Paused = true;
        readonly Random random;
        int nextCustomer = 1, nextEvent = 1;
        float spawnIn = 1, autoIn;
        public bool Rush { get { return P.served>=10 && Time > 120 && Time % 180 > 135; } }
        public bool TutorialActive { get { return !P.tutorialDone; } }
        public int Level { get { return 1 + P.xp / 100; } }
        public float Rating { get { return Math.Max(1, Math.Min(5, P.reviewTotal / P.reviewCount)); } }
        public float Cleanliness { get { return 100 - Tables.Count(t => t.dirty) * 100f / Tables.Count; } }

        public Simulation(Progress progress = null, int seed = 21)
        {
            random = new Random(seed);
            P = progress ?? new Progress();
            Normalize();
            SyncTables();
            // Reopening halfway through the first lesson restores its cleaning step.
            if(TutorialActive && P.served>0 && P.cleaned==0)Tables[0].dirty=true;
            Actors.Add(new Actor { role = "player", label = "Sen", x = 8.1f, z = 4 });
            SyncStaff();
            RefreshDay(DateTime.Now.ToString("yyyy-MM-dd"));
        }
        void Normalize()
        {
            if(P.version<1||P.version>Progress.CurrentVersion)throw new ArgumentException("Desteklenmeyen kayıt sürümü.");
            if(P.version==1)P.tutorialDone=P.served>0||P.cleaned>0||P.lost>0;
            if(P.version<5){P.activeBranch=0;P.londonOpen=false;P.istanbul=null;P.london=null;}
            P.version=Progress.CurrentVersion;P.coins=Math.Max(0,P.coins);P.xp=Math.Max(0,P.xp);P.dailyMenuMask&=127;
            P.dailyReviews=Clamp(P.dailyReviews,0,ReviewCatalog.DailyTarget);
            P.activeBranch=P.activeBranch==1&&P.londonOpen?1:0;
            NormalizeRestaurant(P);
            if(P.istanbul!=null)NormalizeRestaurant(P.istanbul);
            if(P.londonOpen){
                if(P.london==null)P.london=new RestaurantProgress{tutorialDone=true};
                NormalizeRestaurant(P.london);
                if(P.istanbul==null)P.istanbul=new RestaurantProgress{tutorialDone=true,layout=RestaurantLayout.Default()};
            }else P.london=null;
            NormalizeAchievements();PrepareSave();
        }
        static void NormalizeRestaurant(RestaurantProgress p)
        {
            if(p.shift==null)p.shift=new ShiftProgress();p.shift.Normalize();
            if(!LayoutRules.Validate(p.layout,out _))p.layout=RestaurantLayout.Default();
            if(p.cleaned>0||p.cashier+p.cook+p.waiter+p.cleaner+p.manager>0)p.tutorialDone=true;
            p.tables=Clamp(p.tables,2,4);p.grill=Clamp(p.grill,1,3);
            p.cashier=Clamp(p.cashier,0,3);p.cook=Clamp(p.cook,0,3);p.waiter=Clamp(p.waiter,0,3);p.cleaner=Clamp(p.cleaner,0,3);
            p.manager=Clamp(p.manager,0,1);p.cheese=Clamp(p.cheese,0,1);p.decor=Clamp(p.decor,0,1);
            p.reviewCount=Math.Max(1,p.reviewCount);
            if(float.IsNaN(p.reviewTotal)||float.IsInfinity(p.reviewTotal))p.reviewTotal=4.2f*p.reviewCount;
            p.reviewTotal=Math.Max(p.reviewCount,Math.Min(p.reviewCount*5f,p.reviewTotal));
            NormalizeReviews(p);
            p.popularity=Clamp(p.popularity,0,100);p.influencerShares=Math.Max(0,p.influencerShares);
            p.vipArrived=Math.Max(0,p.vipArrived);p.vipServed=Math.Max(0,p.vipServed);p.vipEarned=Math.Max(0,p.vipEarned);p.vipLost=Math.Max(0,p.vipLost);
            if(!ServicePriorityCatalog.Valid(p.servicePriority))p.servicePriority=ServicePriorityCatalog.Arrival;
            p.fryer=Clamp(p.fryer,1,3);p.blender=Clamp(p.blender,0,2);p.doubleBurger=Clamp(p.doubleBurger,0,1);
            p.classicSold=Math.Max(0,p.classicSold);p.cheeseSold=Math.Max(0,p.cheeseSold);
            p.doubleSold=Math.Max(0,p.doubleSold);p.shakeSold=Math.Max(0,p.shakeSold);
            p.chickenBurger=Clamp(p.chickenBurger,0,1);p.bbqBurger=Clamp(p.bbqBurger,0,1);p.chocolateShake=Clamp(p.chocolateShake,0,1);
            p.chickenSold=Math.Max(0,p.chickenSold);p.bbqSold=Math.Max(0,p.bbqSold);p.chocolateSold=Math.Max(0,p.chocolateSold);
            p.served=Math.Max(0,p.served);p.earned=Math.Max(0,p.earned);p.cooked=Math.Max(0,p.cooked);
            p.driveThru=Clamp(p.driveThru,0,2);p.driveServed=Math.Max(0,p.driveServed);p.driveEarned=Math.Max(0,p.driveEarned);p.driveLost=Math.Max(0,p.driveLost);p.driveMission=Clamp(p.driveMission,0,3);
            p.delivery=Clamp(p.delivery,0,2);p.deliveryServed=Math.Max(0,p.deliveryServed);p.deliveryEarned=Math.Max(0,p.deliveryEarned);p.deliveryLost=Math.Max(0,p.deliveryLost);p.deliveryMission=Clamp(p.deliveryMission,0,3);
            p.cleaned=Math.Max(0,p.cleaned);p.lost=Math.Max(0,p.lost);p.mission=Clamp(p.mission,0,11);
        }
        static int Clamp(int n, int lo, int hi) { return Math.Min(hi, Math.Max(lo, n)); }
        public void RefreshDay(string day)
        {
            if (P.day == day) return;
            P.day = day; P.dailyServed = P.dailyCleaned = P.dailyEarned = 0;
            P.dailyServeClaimed = P.dailyCleanClaimed = P.dailyEarnClaimed = false;
            P.dailyMenuMask=0;P.dailyVarietyClaimed=false;
            P.dailyReviews=0;P.dailyReviewClaimed=false;
        }
        void SyncTables()
        {
            while (Tables.Count < P.tables)
            {
                int i = Tables.Count;
                Tables.Add(new Table { id = i, x = P.layout.tables[i].x, z = P.layout.tables[i].z });
            }
        }
        public bool ApplyLayout(RestaurantLayout draft,out string reason)
        {
            if(!Paused){reason="Düzenlemek için vardiyayı duraklat.";return false;}
            if(!LayoutRules.Validate(draft,out reason))return false;
            P.layout=draft.Copy();
            foreach(var t in Tables){t.x=P.layout.tables[t.id].x;t.z=P.layout.tables[t.id].z;}
            foreach(var c in Customers) {
                c.crowd.Reset();
                if(c.drive||c.delivery)continue;
                if(c.table>=0&&(c.phase=="waiting"||c.phase=="eating"||c.phase=="payment")) {
                    var t=Tables[c.table];c.x=t.x;c.z=t.z+.85f;
                } else {
                    // Layout editing is the only time an existing position may need relocation.
                    var free=LayoutRules.NearestFree(P.layout,new Point(c.x,c.z));c.x=free.x;c.z=free.z;
                    c.path.Clear();c.pathBlocked=false;
                    if(c.phase=="walking")SeatRoute(c);
                    else if(c.phase=="leaving")LeaveRoute(c);
                }
            }
            foreach(var a in Actors) {
                a.crowd.Reset();
                var free=LayoutRules.NearestFree(P.layout,new Point(a.x,a.z));a.x=free.x;a.z=free.z;
                var c=Customers.FirstOrDefault(v=>v.id==a.customer);
                if(a.job=="clean"&&a.table>=0){var t=Tables[a.table];Route(a,t.x+1.18f,t.z);}
                else if((a.job=="collect"||a.job=="serve"&&a.stage>=1)&&c!=null){if(c.delivery)Route(a,DeliveryCounterX,DeliveryCounterZ);else if(c.drive)Route(a,DriveWindowX,DriveWindowZ);else{var t=Tables[c.table];Route(a,t.x+1.18f,t.z);}}
                else if(a.job=="")ReturnHome(a);
                else if(a.path.Count>0){var target=a.path[a.path.Count-1];Route(a,target.x,target.z);}
            }
            return true;
        }
        void SyncStaff()
        {
            string[] roles = { "cashier", "cook", "waiter", "cleaner", "manager", "drive" };
            string[] labels = { "Kasiyer", "Aşçı", "Garson", "Temizlikçi", "Müdür", "Araç görevlisi" };
            for (int i = 0; i < roles.Length; i++)
                if (StaffLevel(roles[i]) > 0 && !Actors.Any(a => a.role == roles[i])) {
                    var p=StaffHome(roles[i]);Actors.Add(new Actor { role = roles[i], label = labels[i], x = p.x, z = p.z });
                }
        }
        public int StaffLevel(string role)
        {
            switch (role) { case "cashier": return P.cashier; case "cook": return P.cook; case "waiter": return P.waiter;
                case "cleaner": return P.cleaner; case "manager": return P.manager; case "drive":return P.driveThru; default: return 1; }
        }
        void Emit(string text, string kind = "info", float x = 5, float z = 5)
        {
            Events.Add(new Event { id = nextEvent++, text = text, kind = kind, x = x, z = z });
            if (Events.Count > 8) Events.RemoveAt(0);
        }
        public string Command(string action, string id = "", float x = 0, float z = 0)
        {
            if (action == "pause") { if(P.shift.pending){Paused=true;return "Önce vardiya sonucunu incele.";}Paused = !Paused; return Paused ? "Vardiya duraklatıldı." : "Restoran açık!"; }
            if (action == "buy") return Buy(id);
            if (action == "claim") return Claim(id);
            if (action == "achieve") return ClaimAchievement(id);
            if (Paused) return "Önce vardiyayı başlat.";
            Actor player = Actors[0];
            if (player.job != "") return "Önce elindeki işi bitir.";
            if (action == "move")
            {
                if (float.IsNaN(x) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(z)) return "Geçersiz konum.";
                if(!Route(player,Math.Max(-3.5f,Math.Min(9.4f,x)),Math.Max(.55f,Math.Min(9.3f,z))))return "Oraya açık bir yol yok.";
                player.job = "move";
                return "";
            }
            if (action == "next") action = SuggestedAction();
            if (!Assign(player, action)) return EmptyMessage(action);
            return "";
        }
        static string EmptyMessage(string action)
        {
            switch (action) {
                case "order": return "Sipariş bekleyen kurye, araç veya temiz masaya oturacak müşteri gerekiyor.";
                case "cook": return "Hazırlanacak sipariş yok veya hazırlama istasyonu dolu.";
                case "serve": return "Henüz servise hazır sipariş yok.";
                case "collect": return "Henüz ödeme bekleyen müşteri yok.";
                case "clean": return "Bütün masalar temiz!";
                default: return "Yeni müşterin birazdan geliyor.";
            }
        }
        public string SuggestedAction()
        {
            if (Customers.Any(c => c.phase == "payment" && !c.reserved)) return "collect";
            if (NextServingOrder()!=null) return "serve";
            if (NextCookingOrder()!=null) return "cook";
            if (NextOrder()!=null) return "order";
            if (Tables.Any(t => t.dirty && !t.reserved)||CanTidy) return "clean";
            return "";
        }
        public int CookingSlots(string station)
        {
            int capacity=station=="blender"?P.blender:P.grill;
            int working=Customers.Count(c=>c.station==station&&c.order=="cooking");
            int reserved=Actors.Count(a=>a.job=="cook"&&Customers.Any(c=>c.id==a.customer&&c.station==station&&c.order=="ordered"));
            return Math.Max(0,capacity-working-reserved);
        }
        Customer NextCookingOrder(){return PrioritizedCustomer(c=>c.order=="ordered"&&!c.reserved&&CookingSlots(c.station)>0);}
        public bool CanPerform(string action)
        {
            if(Paused||Actors[0].job!="")return false;
            switch(action) {
                case "order":return NextOrder()!=null;
                case "cook":return NextCookingOrder()!=null;
                case "serve":return NextServingOrder()!=null;
                case "collect":return Customers.Any(c=>c.phase=="payment"&&!c.reserved);
                case "clean":return Tables.Any(t=>t.dirty&&!t.reserved)||CanTidy;
                default:return false;
            }
        }
        bool Assign(Actor actor, string job)
        {
            Customer c = null; Table table = null;
            if (job == "order")
            {
                c=NextOrder(actor);if(c==null)return false;
                if(c.delivery){if(!Route(actor,DeliveryCounterX,DeliveryCounterZ))return false;actor.table=-1;}
                else if(c.drive){if(!Route(actor,DriveWindowX,DriveWindowZ))return false;actor.table=-1;}
                else {
                    table=Tables.First(t=>t.customer==0&&!t.dirty&&!t.reserved);
                    if(!StationRoute(actor,"cashier"))return false;
                    table.reserved=true;actor.table=table.id;
                }
            }
            else if (job == "cook")
            {
                c = NextCookingOrder();
                if (c == null) return false;
                if(!StationRoute(actor,c.station))return false;
            }
            else if (job == "serve")
            {
                c = NextServingOrder(actor);
                if (c == null) return false;
                if(!StationRoute(actor,"pickup"))return false;
            }
            else if (job == "collect")
            {
                c = Customers.FirstOrDefault(v => v.phase == "payment" && !v.reserved);
                if (c == null) return false;
                table = Tables[c.table];if(!Route(actor,table.x+1.18f,table.z+(actor.role=="cashier"?.35f:0)))return false;
            }
            else if (job == "clean")
            {
                table = Tables.FirstOrDefault(t => t.dirty && !t.reserved);
                if (table == null) return AssignTidying(actor);
                if(!Route(actor,table.x+1.18f,table.z))return false;
                table.reserved = true; actor.table = table.id;
            }
            else return false;
            actor.job = job; actor.customer = c == null ? 0 : c.id; actor.stage = 0; actor.timer = 0;
            if (c != null) c.reserved = true;
            return true;
        }
        bool Route(Actor a, float x, float z)
        {
            var from=new Point(a.x,a.z);
            var to=LayoutRules.NearestFree(P.layout,new Point(x,z));
            var path=LayoutRules.Path(P.layout,from,to);if(path==null)return false;
            a.crowd.Reset();a.path.Clear();a.path.AddRange(path);return true;
        }
        void SeatRoute(Customer c)
        {
            var t=Tables[c.table];
            if(!CustomerRoute(c,new Point(t.x+1.18f,t.z+.85f)))return;
            c.path.Add(new Point(t.x,t.z+.85f));
        }
        void LeaveRoute(Customer c)
        {
            // A seated customer steps sideways out of their chair before joining the aisle.
            c.path.Clear();Point from;
            if(c.table>=0&&Math.Abs(c.x-Tables[c.table].x)<.1f&&Math.Abs(c.z-Tables[c.table].z-.85f)<.1f) {
                var t=Tables[c.table];from=new Point(t.x+1.18f,t.z+.85f);c.path.Add(from);
            } else from=new Point(c.x,c.z);
            c.crowd.Reset();
            var route=LayoutRules.Path(P.layout,from,new Point(11.6f,9.25f));c.pathBlocked=route==null;if(route!=null)c.path.AddRange(route);
        }
        bool CustomerRoute(Customer c,Point target)
        {
            var route=LayoutRules.Path(P.layout,new Point(c.x,c.z),target);
            c.crowd.Reset();c.path.Clear();c.pathBlocked=route==null;if(route!=null)c.path.AddRange(route);return route!=null;
        }
        bool WalkCustomer(Customer c,Point target,float amount)
        {
            if(TickCrowdDetour(c,c.crowd,ref c.x,ref c.z,amount))return false;
            if(WorkZones.Distance(new Point(c.x,c.z),target)<.001f)return true;
            if(c.path.Count==0||WorkZones.Distance(c.path[c.path.Count-1],target)>.001f)
                if(!CustomerRoute(c,target))return false;
            return Follow(c,amount);
        }
        bool Follow(Customer c,float amount)
        {
            if(!c.drive&&TickCrowdDetour(c,c.crowd,ref c.x,ref c.z,amount))return false;
            if(c.pathBlocked)return false;
            if(c.path.Count==0)return true;
            var p=c.path[0];
            // Cars use the road; every pedestrian segment must clear the same fixed solids.
            if(!c.drive&&!LayoutRules.ClearStructure(new Point(c.x,c.z),p)){c.pathBlocked=true;return false;}
            if(c.drive){if(Move(ref c.x,ref c.z,p.x,p.z,amount))c.path.RemoveAt(0);}
            else if(c.crowd.detour.Count>0) {
                if(CrowdStep(c,c.crowd,ref c.x,ref c.z,c.crowd.detour[0],amount))c.crowd.detour.RemoveAt(0);
            } else if(CrowdStep(c,c.crowd,ref c.x,ref c.z,p,amount,c.table>=0&&(c.phase=="walking"||c.phase=="leaving")&&
                (!LayoutRules.Walkable(P.layout,new Point(c.x,c.z))||!LayoutRules.Walkable(P.layout,p))))c.path.RemoveAt(0);
            return c.path.Count==0;
        }
        static bool Move(ref float x, ref float z, float tx, float tz, float amount)
        {
            float dx = tx - x, dz = tz - z, distance = (float)Math.Sqrt(dx * dx + dz * dz);
            if (distance <= amount) { x = tx; z = tz; return true; }
            x += dx / distance * amount; z += dz / distance * amount; return false;
        }
        public void Tick(float dt)
        {
            if(P.shift.pending){Paused=true;return;}
            if (Paused || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            int shiftServed=P.served,shiftLost=P.lost,shiftEarned=P.earned,shiftReviews=P.reviewCount;float shiftRating=P.reviewTotal;
            dt = Math.Min(.25f, dt);
            if(P.shift.active)dt=Math.Min(dt,ShiftProgress.Duration-P.shift.elapsed);
            Time += dt;
            spawnIn -= dt;
            if (spawnIn <= 0)
            {
                if (Customers.Count(c => !c.drive&&!c.delivery&&(c.phase == "arriving" || c.phase == "queue")) < 4 && (!TutorialActive || (Customers.Count==0 && P.served==0))) Spawn();
                spawnIn = (Economy.ArrivalSeconds(P.served,Rush) + random.Next(3)) * (P.activeBranch==1?.85f:1) * PopularityCatalog.ArrivalMultiplier(P);
            }
            TickDrive(dt);TickDelivery(dt);TickOperations(dt);
            foreach (Customer c in Customers.ToArray()) UpdateCustomer(c, dt);
            foreach (Actor a in Actors) UpdateActor(a, dt);
            autoIn -= dt;
            if (autoIn <= 0)
            {
                autoIn = .4f;
                foreach (Actor a in Actors.Where(v => v.role != "player" && v.job == ""&&v.crowd.detour.Count==0))
                {
                    if (a.role == "cashier"&&!Assign(a,"order"))Assign(a,"collect");
                    if (a.role == "cook") Assign(a, "cook");
                    if (a.role == "waiter" && !Assign(a, "serve")&&!Assign(a,"collect")&&P.cleaner==0)Assign(a,"clean");
                    if (a.role == "cleaner") Assign(a, "clean");
                    if (a.role == "drive"&&!Assign(a,"serve"))Assign(a,"order");
                }
            }
            RecordShift(dt,shiftServed,shiftLost,shiftEarned,shiftReviews,shiftRating);
        }
        void Spawn()
        {
            // A previous guest may still be leaving near the entrance. Retry later, never spawn inside a body.
            if(Pedestrians().Any(p=>WorkZones.Distance(new Point(p.x,p.z),new Point(10.7f,8.5f))<PersonalSpace+.12f))return;
            int kind = nextCustomer == 1 ? 0 : PopularityCatalog.CustomerKind(P,P.activeBranch,random.Next(100),random.Next(5),Rush);
            string[] names = P.activeBranch==1?new[]{"Alex","Emily","James","Oliver","Sofia","Charlie","Morgan"}:new[]{"Deniz","Ece","Ali","Mert","Ada","Can","Selin"};
            float[] patience = { 100, 65, 120, 80, 110, 90 };
            var recipes=MenuCatalog.All(P).Where(r=>r.available).ToList();
            var recipe=TutorialActive?recipes[0]:CustomerProfiles.Select(P,kind,random.Next());
            float p = kind==VipCatalog.Kind?VipCatalog.Patience(P):patience[kind] + P.decor * 25;
            if (P.served == 0) p = 240;
            Customers.Add(new Customer { id = nextCustomer++, kind = kind, name = names[kind],
                patience = p, maxPatience = p, price = kind==VipCatalog.Kind?VipCatalog.Price(recipe.price):recipe.price + (kind == 2 ? recipe.familyExtra : 0),
                product=recipe.id,station=recipe.station,meal=recipe.name });
            if(kind==VipCatalog.Kind){P.vipArrived=VipCatalog.Add(P.vipArrived,1);Emit("VIP misafir geldi! Hızlı servis bekliyor.","vip",10.7f,8.5f);}
        }
        void UpdateCustomer(Customer c, float dt)
        {
            if(c.order=="ready"||c.order=="packing")c.readySeconds+=dt;
            if(c.phase=="payment")c.paymentSeconds+=dt;
            if(c.drive){UpdateDrive(c,dt);return;}
            if(c.delivery){UpdateDelivery(c,dt);return;}
            if (c.phase == "arriving" || c.phase == "queue")
            {
                int index = Customers.Where(v => !v.drive&&!v.delivery&&(v.phase == "arriving" || v.phase == "queue")).ToList().IndexOf(c);
                if (WalkCustomer(c,new Point(9.1f,3.4f+index*.85f),dt*2.2f)) c.phase = "queue";
            }
            if (c.phase == "walking")
            {
                if (Follow(c,dt*2.2f)) c.phase = "waiting";
            }
            if (c.phase == "queue" || c.phase == "waiting" || c.phase == "walking")
            {
                if(!TutorialActive)c.patience -= dt;
                if (c.patience <= 0) { Lose(c); return; }
            }
            UpdateCooking(c,dt);
            if (c.phase == "eating") { c.timer -= dt; if (c.timer <= 0) c.phase = "payment"; }
            if (c.phase == "leaving" && Follow(c,dt*2.6f)) Customers.Remove(c);
        }
        void Lose(Customer c)
        {
            if(c.phase=="leaving")return;
            float cleanliness=Cleanliness;
            if (c.table >= 0) { Tables[c.table].customer = 0; Tables[c.table].dirty = true; }
            foreach (Actor a in Actors.Where(a => a.customer == c.id)) Cancel(a);
            c.reserved = false; c.phase = "leaving"; c.order = "none";
            if(c.delivery){P.deliveryLost++;DeliveryExit(c);}else if(c.drive){P.driveLost++;DriveExit(c,true);}else LeaveRoute(c);
            P.lost++;
            if(VipCatalog.IsVip(c))P.vipLost=VipCatalog.Add(P.vipLost,1);
            RecordReview(c,false,cleanliness);
            Emit(VipCatalog.IsVip(c)?"VIP misafir beklemekten vazgeçti…":"Bekleme çok uzundu…", "bad", c.x, c.z);
        }
        void Cancel(Actor a)
        {
            if (a.table >= 0) Tables[a.table].reserved = false;
            Customer c = Customers.FirstOrDefault(v => v.id == a.customer); if (c != null) c.reserved = false;
            a.job = ""; a.customer = 0; a.table = -1; a.path.Clear(); a.timer = 0;a.crowd.Reset();ReturnHome(a);
        }
        void UpdateActor(Actor a, float dt)
        {
            float speed = 4.4f * (1 + P.manager * .15f + (StaffLevel(a.role) - 1) * .12f);
            if(TickCrowdDetour(a,a.crowd,ref a.x,ref a.z,dt*speed))return;
            if (a.path.Count > 0)
            {
                Point p = a.path[0];
                if(!LayoutRules.ClearStructure(new Point(a.x,a.z),p)){Cancel(a);return;}
                if(DoorBlocks(a,p,dt*speed))return;
                if(CrowdStep(a,a.crowd,ref a.x,ref a.z,p,dt*speed))a.path.RemoveAt(0);
                return;
            }
            if (a.job == "") return;
            a.timer += dt;
            if(a.job=="inspect"){if(a.timer>=3)Cancel(a);return;}
            if(a.job=="sweep"||a.job=="trash") {
                if(a.timer<1.8f)return;
                if(a.job=="sweep"&&FloorMess.Count>0)FloorMess.RemoveAt(0);else if(a.job=="trash")Trash=0;
                Cancel(a);return;
            }
            if (a.timer < (a.job == "clean" ? 1.3f : .35f)) return;
            Customer c = Customers.FirstOrDefault(v => v.id == a.customer);
            if (a.job == "serve" && a.stage == 0 && c != null)
            {
                a.stage = 1; a.timer = 0;
                if(c.delivery)Route(a,DeliveryCounterX,DeliveryCounterZ);
                else if(c.drive)Route(a,DriveWindowX,DriveWindowZ);
                else {Table table = Tables[c.table]; Route(a, table.x + 1.18f, table.z);} return;
            }
            if(a.job=="serve"&&c!=null&&(c.delivery||c.drive)) {
                if(a.stage==1){a.stage=2;a.timer=0;c.order="packing";return;}
                if(a.stage==2&&(a.timer<(c.delivery?DeliveryPackingSeconds:1)||c.delivery&&!AtDeliveryPickup(c)))return;
            }
            if (a.job == "order" && c != null)
            {
                c.order="ordered";
                if(c.drive||c.delivery)c.phase="waiting";
                else {
                    Table t=Tables[a.table];t.customer=c.id;t.reserved=false;
                    c.table=t.id;c.phase="walking";SeatRoute(c);
                }
                Emit("Bir " + c.meal.ToLower() + "!", "order", c.x, c.z);
            }
            if (a.job == "cook" && c != null) {
                if(a.stage==0){c.order="cooking";c.cookTime=MenuCatalog.PrepSeconds(MenuCatalog.Get(c.product,P),P)*(CustomerProfiles.Portions(c)==2?1.35f:1);}
                if(c.station!="blender") {
                    if(a.stage==0){a.stage=1;a.timer=0;StationRoute(a,"fryer");return;}
                    if(a.stage==1){if(a.timer<1.1f)return;a.stage=2;a.timer=0;StationRoute(a,"prep");return;}
                    if(a.stage==2&&a.timer<.7f)return;
                }
            }
            if (a.job == "serve" && c != null)
            {
                c.order="served";
                if(c.drive||c.delivery)TakePayment(c);
                else {c.phase="eating";c.timer=CustomerProfiles.Portions(c)==2?8:5.5f;Emit("Afiyet olsun!","happy",c.x,c.z);}
            }
            if (a.job == "collect" && c != null)TakePayment(c);
            if (a.job == "clean") {
                Table t=Tables[a.table];t.dirty=false;P.cleaned++;P.dailyCleaned++;P.xp+=5;
                RefreshAchievements();
                if(TutorialActive&&P.served>0){P.tutorialDone=true;spawnIn=2;Emit("İlk vardiyan tamam! Görevler ekranından ödülünü al.","upgrade",t.x,t.z);}
                else Emit("Pırıl pırıl!","clean",t.x,t.z);
            }
            Cancel(a);
        }
        public List<Offer> Offers()
        {
            var offers=new List<Offer> {
                new Offer { id="table", name="Yeni masa", description="Salonda 1 yeni masa · daha fazla müşteri", icon="table", cost=100+(P.tables-2)*90, level=P.tables, max=4 },
                new Offer { id="grill", name="Izgara", description="Aynı anda +1 sipariş · daha hızlı pişirme", icon="grill", cost=P.grill*150, level=P.grill, max=3 },
                new Offer { id="cashier", name="Kasiyer", description="Siparişleri otomatik alır · her seviyede yürüme hızı +%12", icon="cashier", cost=Economy.StaffCost(175,P.cashier), level=P.cashier, max=3 },
                new Offer { id="cook", name="Aşçı", description="Otomatik hazırlık · her seviyede pişirme ve yürüme hızı artar", icon="cook", cost=Economy.StaffCost(260,P.cook), level=P.cook, max=3 },
                new Offer { id="waiter", name="Garson", description="Servis ve ödeme otomatik · her seviyede yürüme hızı +%12", icon="waiter", cost=Economy.StaffCost(220,P.waiter), level=P.waiter, max=3 },
                new Offer { id="cleaner", name="Temizlik görevlisi", description="Masaları otomatik temizler · her seviyede yürüme hızı +%12", icon="cleaner", cost=Economy.StaffCost(140,P.cleaner), level=P.cleaner, max=3 },
                new Offer { id="manager", name="Müdür", description="Personel hızı +%15 · gelir +%10", icon="manager", cost=450, level=P.manager, max=1 },
                new Offer { id="cheese", name="Cheeseburger", description=MenuCatalog.Get("cheese",P).price+" coin değerinde yeni menü", icon="burger", cost=180, level=P.cheese, max=1 },
                new Offer { id="decor", name="Yeşil dokunuş", description="Yeni bitkiler · müşteri sabrı +25 saniye", icon="plant", cost=90, level=P.decor, max=1 },
                new Offer {id="blender",name="Milkshake makinesi",description=P.blender==0?"Çilekli Milkshake açılır · ızgaradan bağımsız üretim":"2 içecek aynı anda · hazırlama hızına +%30",icon="blender",cost=P.blender==0?250:350,level=P.blender,max=2},
                new Offer {id="fryer",name="Fritöz",description="Burger menülerinin hazırlama hızına seviye başına +%15",icon="fryer",cost=P.fryer*180,level=P.fryer,max=3},
                new Offer {id="drive",name="Arabaya servis",description=P.driveThru==0?"3 araçlık kuyruk · paket fiyatı +%20 · masa kullanmaz":"4 araçlık kuyruk · daha sık geliş · sürücü sabrı +20 sn",icon="car",cost=P.driveThru==0?600:450,level=P.driveThru,max=2},
                new Offer {id="delivery",name="Kurye teslimatı",description=P.delivery==0?"2 kurye alanı · paketleme tezgâhı · fiyat +%15":"3 kurye alanı · paketleme 0,8 sn · daha sık geliş",icon="bag",cost=P.delivery==0?750:500,level=P.delivery,max=2},
                new Offer {id="double",name="Çift Katlı Menü",description=MenuCatalog.Get("double",P).price+" coin · iki köfte, patates ve kola",icon="burger",cost=320,level=P.doubleBurger,max=1},
                new Offer {id="chicken",name="Tavuk Burger Menü",description=MenuCatalog.Get("chicken",P).price+" coin · ızgara tavuk, patates ve kola",icon="chicken",cost=220,level=P.chickenBurger,max=1},
                new Offer {id="bbq",name="Barbekü Menü",description=MenuCatalog.Get("bbq",P).price+" coin · özel barbekü sosu, patates ve kola",icon="bbq",cost=380,level=P.bbqBurger,max=1},
                new Offer {id="chocolate",name="Çikolatalı Milkshake",description=MenuCatalog.Get("chocolate",P).price+" coin · mevcut milkshake makinesinde yeni tarif",icon="chocolate",cost=280,level=P.chocolateShake,max=1}
            };
            foreach(var o in offers) {
                if(o.id=="blender"&&Level<3)o.lockedReason="Seviye 3 gerekiyor.";
                if(o.id=="fryer"&&Level<3)o.lockedReason="Seviye 3 gerekiyor.";
                if(o.id=="drive")o.lockedReason=Level<6?"Seviye 6 gerekiyor.":P.grill<2?"Önce ızgarayı seviye 2 yap.":!P.tutorialDone?"Önce ilk vardiyanı tamamla.":"";
                if(o.id=="delivery")o.lockedReason=Level<7?"Seviye 7 gerekiyor.":P.grill<2?"Önce ızgarayı seviye 2 yap.":P.cook<1?"Önce bir aşçı işe al.":!P.tutorialDone?"Önce ilk vardiyanı tamamla.":"";
                if(o.id=="double")o.lockedReason=Level<5?"Seviye 5 gerekiyor.":P.grill<2?"Önce ızgarayı seviye 2 yap.":P.cheese<1?"Önce Cheeseburger menüsünü aç.":"";
                if(o.id=="chicken")o.lockedReason=Level<4?"Seviye 4 gerekiyor.":P.cheese<1?"Önce Cheeseburger menüsünü aç.":"";
                if(o.id=="bbq")o.lockedReason=Level<7?"Seviye 7 gerekiyor.":P.grill<2?"Önce ızgarayı seviye 2 yap.":P.doubleBurger<1?"Önce Çift Katlı Menü'yü aç.":"";
                if(o.id=="chocolate")o.lockedReason=Level<6?"Seviye 6 gerekiyor.":P.blender<1?"Önce milkshake makinesi al.":"";
            }
            return offers;
        }
        string Buy(string id)
        {
            Offer o = Offers().FirstOrDefault(v => v.id == id);
            if (o == null) return "Bilinmeyen geliştirme.";
            if (o.level >= o.max) return "En yüksek seviyede.";
            if (!string.IsNullOrEmpty(o.lockedReason)) return o.lockedReason;
            if (P.coins < o.cost) return "Bu geliştirme için " + (o.cost - P.coins) + " coin daha gerekiyor.";
            P.coins -= o.cost; P.xp += 15;
            switch (id) { case "table": P.tables++; break; case "grill": P.grill++; break; case "cashier": P.cashier++; break;
                case "cook": P.cook++; break; case "waiter": P.waiter++; break; case "cleaner": P.cleaner++; break;
                case "manager": P.manager++; break; case "cheese": P.cheese++; break; case "decor": P.decor++; break;
                case "delivery":P.delivery++;if(P.delivery==1)deliverySpawnIn=6;break;
                case "drive":P.driveThru++;if(P.driveThru==1)driveSpawnIn=4;break;
                case "blender":P.blender++;break;case "fryer":P.fryer++;break;case "double":P.doubleBurger++;break;
                case "chicken":P.chickenBurger++;break;case "bbq":P.bbqBurger++;break;case "chocolate":P.chocolateShake++;break; }
            SyncTables(); SyncStaff(); RefreshAchievements();Emit(o.name + " geliştirildi!", "upgrade");
            return o.name + " hazır!";
        }
        public List<Mission> Missions()
        {
            string[] titles = { "İlk müşterinin ödemesini al", "Üçüncü masanı aç", "İlk kasiyerini işe al", "10 müşteriye servis yap", "İlk aşçını işe al",
                "Milkshake makinesi al", "5 milkshake sat", "Fritözü seviye 2 yap", "Çift Katlı Menü'yü aç", "5 çift katlı menü sat", "Dördüncü masanı aç" };
            int[] values = { P.served, P.tables, P.cashier, P.served, P.cook,P.blender,P.shakeSold,P.fryer,P.doubleBurger,P.doubleSold,P.tables },
                goals = { 1, 3, 1, 10, 1,1,5,2,1,5,4 }, rewards = { 80, 100, 100, 180, 200,150,180,180,220,260,250 };
            if(P.activeBranch==1) {
                titles=new[]{"Londra'da 5 müşteriye servis yap","Londra için kasiyer işe al","Londra için aşçı işe al","Londra'ya milkshake makinesi al","Londra'da 5 milkshake sat","Londra'da 25 müşteriye servis yap"};
                values=new[]{P.served,P.cashier,P.cook,P.blender,P.shakeSold,P.served};
                goals=new[]{5,1,1,1,5,25};rewards=new[]{100,100,150,150,180,250};
            }
            var list = new List<Mission>();
            if (P.mission < titles.Length) list.Add(new Mission { id="main", title=titles[P.mission], current=values[P.mission], target=goals[P.mission], reward=rewards[P.mission] });
            if((Level>=6||P.driveThru>0)&&P.driveMission<3) {
                string[] driveTitles={"Bu şubede arabaya servisi aç","10 araca paket teslim et","Arabaya servisi seviye 2 yap"};
                int[] driveValues={P.driveThru,P.driveServed,P.driveThru},driveGoals={1,10,2},driveRewards={120,180,200};
                list.Add(new Mission{id="drive",title=driveTitles[P.driveMission],current=driveValues[P.driveMission],target=driveGoals[P.driveMission],reward=driveRewards[P.driveMission]});
            }
            if((Level>=7||P.delivery>0)&&P.deliveryMission<3) {
                string[] deliveryTitles={"Bu şubede kurye teslimatını aç","10 kurye paketi teslim et","Kurye teslimatını seviye 2 yap"};
                int[] deliveryValues={P.delivery,P.deliveryServed,P.delivery},deliveryGoals={1,10,2},deliveryRewards={150,200,220};
                list.Add(new Mission{id="delivery",title=deliveryTitles[P.deliveryMission],current=deliveryValues[P.deliveryMission],target=deliveryGoals[P.deliveryMission],reward=deliveryRewards[P.deliveryMission]});
            }
            list.Add(new Mission { id="dailyServe", title="10 mutlu müşteri", current=P.dailyServed, target=10, reward=150, claimed=P.dailyServeClaimed });
            list.Add(new Mission { id="dailyClean", title="5 masayı temizle", current=P.dailyCleaned, target=5, reward=100, claimed=P.dailyCleanClaimed });
            list.Add(new Mission { id="dailyEarn", title="500 coin kazan", current=P.dailyEarned, target=500, reward=150, claimed=P.dailyEarnClaimed });
            list.Add(new Mission { id="dailyReview", title="Bugün 5 olumlu yorum al (4★+)", current=P.dailyReviews, target=ReviewCatalog.DailyTarget, reward=ReviewCatalog.DailyReward, claimed=P.dailyReviewClaimed });
            if(MenuCatalog.All(P).Count(r=>r.available)>=3||MenuCatalog.Variety(P.dailyMenuMask)>=3||P.dailyVarietyClaimed)
                list.Add(new Mission{id="dailyVariety",title="Bugün 3 farklı ürün sat",current=MenuCatalog.Variety(P.dailyMenuMask),target=3,reward=180,claimed=P.dailyVarietyClaimed});
            return list;
        }
        string Claim(string id)
        {
            Mission m = Missions().FirstOrDefault(v => v.id == id);
            if (m == null || m.claimed || m.current < m.target) return "Görev henüz tamamlanmadı veya ödülü alındı.";
            P.coins += m.reward; P.xp += 25;
            if (id == "main") P.mission++;
            if (id == "drive") P.driveMission++;
            if (id == "delivery") P.deliveryMission++;
            if (id == "dailyServe") P.dailyServeClaimed = true;
            if (id == "dailyClean") P.dailyCleanClaimed = true;
            if (id == "dailyEarn") P.dailyEarnClaimed = true;
            if (id == "dailyReview") P.dailyReviewClaimed = true;
            if (id == "dailyVariety") P.dailyVarietyClaimed = true;
            Emit("Görev tamam! +" + m.reward + " coin", "upgrade"); return "Ödülün eklendi!";
        }
        public Snapshot View()
        {
            string next = SuggestedAction();
            string hint = "Yeni müşterilerini beklerken restoranını geliştirebilirsin.";
            if (next == "order") hint = NextOrder().delivery?"Kurye geldi. Sipariş al düğmesine dokun.":NextOrder().drive?"Pencerede araç var. Sipariş al düğmesine dokun.":"Müşterini karşıla. Sipariş almak için kasaya git.";
            if (next == "cook") hint = "Sipariş geldi! "+NextCookingOrder().meal+" için Hazırla'ya dokun.";
            if (next == "serve") hint = NextServingOrder().delivery?"Kurye menüsü hazır. Servis et; tezgahta paketleyip teslim et.":NextServingOrder().drive?"Araç paketi hazır. Servis et; ödeme teslimde alınır!":"Menü hazır. Sıcakken müşterine servis et!";
            if (next == "collect") hint = "Ödeme hazır! Kazancını ve bahşişini topla.";
            if (next == "clean") hint = "Yeni müşteriler için kirli masayı temizle.";
            if (Actors[0].job != "") hint = "İş başındasın. Karakterin görevini tamamlıyor.";
            int step=0;string lesson="";
            if(TutorialActive) {
                var first=Customers.FirstOrDefault(c=>c.phase!="leaving");step=1;lesson="Müşterini karşıla";
                hint=first==null||first.phase=="arriving"?"İlk müşterin geliyor. Öğrenirken sabrı azalmayacak.":"Sipariş al düğmesine dokun; karakterin kasaya gitsin.";
                if(first!=null&&first.order!="none") {
                    step=2;lesson="Burgeri hazırla";hint="Hazırla düğmesiyle ızgarayı çalıştır.";
                    if(first.order=="cooking")hint="Burger pişiyor. Hazır olduğunda Servis et düğmesi yanacak.";
                    if(first.order=="ready"){step=3;lesson="Sıcakken servis et";hint="Servis et düğmesine dokun ve menüyü masaya götür.";}
                    if(first.order=="served"){step=4;lesson="İlk kazancını topla";hint=first.phase=="eating"?"Müşterin yemeğini yiyor. Bitirince ödemeyi al.":"Ödeme al düğmesine dokun; coin ve bahşiş kazan.";}
                }
                if(P.served>0){step=5;lesson="Masayı temizle";hint="Temizle düğmesine dokun; ardından Görevler'den ödülünü al.";}
                if(Actors[0].job!="")hint="Karakterin işi tamamlıyor. İlk müşterin seni bekleyecek.";
            }
            return new Snapshot { progress=P, customers=Customers, actors=Actors, tables=Tables, events=Events,
                offers=Offers(), missions=Missions(), time=Time, rating=Rating, cleanliness=Cleanliness, paused=Paused, rush=Rush,
                hint=hint, suggestedAction=next, playerJob=Actors[0].job, level=Level,tutorialStep=step,tutorialTitle=lesson,
                queue=Customers.Count(c=>c.phase=="queue"), ready=Customers.Count(c=>c.order=="ready") };
        }
    }
    public static class Economy
    {
        // Preserve accessible first hires; charge more for later efficiency levels.
        public static int StaffCost(int firstHire,int currentLevel)
        {return (int)(Math.Round(firstHire*(1+currentLevel*.70+currentLevel*currentLevel*.25)/5)*5);}
        public static float ArrivalSeconds(int served,bool rush)
        {return rush?8:served<5?20:served<15?17:14;}
    }
}
