using System;
using System.Collections.Generic;

namespace BurgerEmpire.Core
{
    [Serializable] public sealed class RestaurantLayout
    {
        public Point[] tables;
        // The welcome plant is free; the other two belong to the decoration upgrade.
        public Point[] plants;
        public int floor, wall, furniture;
        public static RestaurantLayout Default()
        {
            return new RestaurantLayout {
                tables=new[]{new Point(2.3f,5.1f),new Point(6,5.1f),new Point(2.3f,7.8f),new Point(6,7.8f)},
                plants=new[]{new Point(.55f,9.45f),new Point(4.15f,5.5f),new Point(4.15f,8.6f)}
            };
        }
        public RestaurantLayout Copy()
        {
            var copy=new RestaurantLayout {floor=floor,wall=wall,furniture=furniture,tables=new Point[4],plants=new Point[3]};
            for(int i=0;i<4;i++)copy.tables[i]=new Point(tables[i].x,tables[i].z);
            for(int i=0;i<3;i++)copy.plants[i]=new Point(plants[i].x,plants[i].z);
            return copy;
        }
    }

    public static class LayoutRules
    {
        public static float Snap(float n) { return (float)Math.Round(n*4,MidpointRounding.AwayFromZero)/4; }
        static bool Finite(float n) { return !float.IsNaN(n)&&!float.IsInfinity(n); }
        public static bool Validate(RestaurantLayout layout,out string reason)
        {
            reason="";
            if(layout==null||layout.tables==null||layout.tables.Length!=4||layout.plants==null||layout.plants.Length!=3){reason="Yerleşim verisi eksik.";return false;}
            if(layout.floor<0||layout.floor>2||layout.wall<0||layout.wall>2||layout.furniture<0||layout.furniture>2){reason="Geçersiz stil.";return false;}
            for(int i=0;i<7;i++) {
                Point p=i<4?layout.tables[i]:layout.plants[i-4];
                if(p==null||!Finite(p.x)||!Finite(p.z)){reason="Geçersiz konum.";return false;}
                bool table=i<4;
                if(p.x<(table?1.15f:.5f)||p.x>(table?7.15f:7.8f)||p.z<(table?4.95f:4f)||p.z>(table?8.6f:9.5f)) {
                    reason="Mutfak, giriş ve sağ servis yolunu boş bırak.";return false;
                }
            }
            var boxes=Obstacles(layout,false);
            for(int i=0;i<boxes.Count;i++)for(int j=i+1;j<boxes.Count;j++)
                if(boxes[i].Overlaps(boxes[j])){reason="Eşyalar veya ayrılmış yeni masa / dekor alanları çakışıyor.";return false;}
            for(int i=0;i<4;i++) {
                Point t=layout.tables[i];
                if(Path(layout,new Point(8.6f,3.8f),new Point(t.x+1.18f,t.z))==null ||
                    Path(layout,new Point(8.6f,3.8f),new Point(t.x+1.18f,t.z+.85f))==null) {
                    reason="Her masanın sağında servis ve oturma yolu bırak.";return false;
                }
            }
            return true;
        }
        // Rectangular clearance envelopes include both chairs. Future purchases retain
        // their space, so buying an item never silently displaces a saved layout.
        struct Bounds
        {
            public float left,right,bottom,top;
            public Bounds(float x,float z,float hw,float hd){left=x-hw;right=x+hw;bottom=z-hd;top=z+hd;}
            public bool Contains(Point p){return p.x>left+.0001f&&p.x<right-.0001f&&p.z>bottom+.0001f&&p.z<top-.0001f;}
            public bool Overlaps(Bounds b){return left<b.right-.001f&&right>b.left+.001f&&bottom<b.top-.001f&&top>b.bottom+.001f;}
            public bool Blocks(Point a,Point b)
            {
                float lo=0,hi=1;
                return Clip(a.x,b.x-a.x,left+.0001f,right-.0001f,ref lo,ref hi)&&Clip(a.z,b.z-a.z,bottom+.0001f,top-.0001f,ref lo,ref hi)&&lo<hi;
            }
            static bool Clip(float v,float d,float min,float max,ref float lo,ref float hi)
            {
                if(Math.Abs(d)<.00001f)return v>min&&v<max;
                float a=(min-v)/d,b=(max-v)/d;if(a>b){float t=a;a=b;b=t;}
                lo=Math.Max(lo,a);hi=Math.Min(hi,b);return lo<hi;
            }
        }
        static List<Bounds> Obstacles(RestaurantLayout l,bool kitchen)
        {
            var boxes=new List<Bounds>();
            foreach(Point p in l.tables)boxes.Add(new Bounds(p.x,p.z,.98f,1.27f));
            for(int i=0;i<3;i++){Point p=l.plants[i];float r=i==0?.48f:.39f;boxes.Add(new Bounds(p.x,p.z,r,r));}
            if(kitchen) {
                boxes.AddRange(structures);
            }
            return boxes;
        }
        static bool Free(Point p,List<Bounds> boxes)
        {
            if(!NavigationGeometry.OnFloor(p))return false;
            foreach(var b in boxes)if(b.Contains(p))return false;
            return true;
        }
        static float Distance(Point a,Point b){float x=a.x-b.x,z=a.z-b.z;return (float)Math.Sqrt(x*x+z*z);}
        static readonly List<Bounds> structures=BuildStructures();
        static List<Bounds> BuildStructures()
        {
            var result=new List<Bounds>();
            foreach(var solid in NavigationGeometry.Solids)result.Add(new Bounds(solid.x,solid.z,solid.width/2+solid.clearance,solid.depth/2+solid.clearance));
            return result;
        }
        static bool Visible(Point a,Point b,List<Bounds> boxes)
        {if(!NavigationGeometry.FloorSegment(a,b))return false;foreach(var rect in boxes)if(rect.Blocks(a,b))return false;return true;}
        public static bool ClearStructure(Point from,Point to){return Visible(from,to,structures);}
        public static bool Walkable(RestaurantLayout layout,Point point){return Free(point,Obstacles(layout,true));}
        public static bool ClearWalkSegment(RestaurantLayout layout,Point from,Point to){return Visible(from,to,Obstacles(layout,true));}
        public static Point NearestFree(RestaurantLayout layout,Point p)
        {
            var boxes=Obstacles(layout,true);
            var target=new Point(Math.Max(-3.6f,Math.Min(9.5f,p.x)),Math.Max(.55f,Math.Min(9.6f,p.z)));
            if(Free(target,boxes))return target;
            Point best=new Point(8.6f,3.8f);float distance=Distance(p,best);
            for(float x=-3.5f;x<=9.5f;x+=.25f)for(float z=.75f;z<=9.5f;z+=.25f) {
                var candidate=new Point(x,z);float d=Distance(p,candidate);
                if(d<distance&&Free(candidate,boxes)){best=candidate;distance=d;}
            }
            return best;
        }
        // Small visibility graph: paths turn around furniture corners and the counter.
        // This runs only when a job or layout changes, never for every animation frame.
        public static List<Point> Path(RestaurantLayout layout,Point from,Point to)
        {
            var boxes=Obstacles(layout,true);
            return FindPath(from,to,boxes);
        }
        public static List<Point> PathAroundPeople(RestaurantLayout layout,Point from,Point to,List<Point> people,float clearance)
        {
            var boxes=Obstacles(layout,true);
            return FindPath(from,to,boxes,people,clearance);
        }
        static bool PeopleFree(Point p,List<Point> people,float clearance)
        {
            if(people!=null)foreach(var person in people)if(Distance(p,person)<clearance-.002f)return false;
            return true;
        }
        static bool PeopleVisible(Point a,Point b,List<Point> people,float clearance)
        {
            if(people==null)return true;
            float dx=b.x-a.x,dz=b.z-a.z,length=dx*dx+dz*dz;
            foreach(var person in people) {
                float t=length<.000001f?0:Math.Max(0,Math.Min(1,((person.x-a.x)*dx+(person.z-a.z)*dz)/length));
                float x=person.x-a.x-t*dx,z=person.z-a.z-t*dz;
                if(x*x+z*z<(clearance-.002f)*(clearance-.002f))return false;
            }
            return true;
        }
        static List<Point> FindPath(Point from,Point to,List<Bounds> boxes,List<Point> people=null,float clearance=0)
        {
            if(!Free(from,boxes)||!Free(to,boxes)||!PeopleFree(from,people,clearance)||!PeopleFree(to,people,clearance))return null;
            if(Visible(from,to,boxes)&&PeopleVisible(from,to,people,clearance))return new List<Point>{new Point(to.x,to.z)};
            var nodes=new List<Point>{from,to};
            foreach(var b in boxes)foreach(float x in new[]{b.left-.025f,b.right+.025f})foreach(float z in new[]{b.bottom-.025f,b.top+.025f}) {
                var point=new Point(x,z);if(Free(point,boxes)&&PeopleFree(point,people,clearance))nodes.Add(point);
            }
            if(people!=null)foreach(var person in people)for(int i=0;i<8;i++) {
                double angle=i*Math.PI/4;float radius=clearance*1.1f+.025f;
                var point=new Point(person.x+(float)Math.Cos(angle)*radius,person.z+(float)Math.Sin(angle)*radius);
                if(Free(point,boxes)&&PeopleFree(point,people,clearance))nodes.Add(point);
            }
            int n=nodes.Count;var distances=new float[n];var previous=new int[n];var visited=new bool[n];
            for(int i=0;i<n;i++){distances[i]=float.PositiveInfinity;previous[i]=-1;}distances[0]=0;
            for(int step=0;step<n;step++) {
                int at=-1;for(int i=0;i<n;i++)if(!visited[i]&&(at<0||distances[i]<distances[at]))at=i;
                if(at<0||float.IsPositiveInfinity(distances[at]))break;
                if(at==1){var path=new List<Point>();for(int i=1;i!=0;i=previous[i])path.Insert(0,new Point(nodes[i].x,nodes[i].z));return path;}
                visited[at]=true;
                for(int i=0;i<n;i++)if(!visited[i]&&Visible(nodes[at],nodes[i],boxes)&&PeopleVisible(nodes[at],nodes[i],people,clearance)) {
                    float d=distances[at]+Distance(nodes[at],nodes[i]);
                    if(d<distances[i]){distances[i]=d;previous[i]=at;}
                }
            }
            return null;
        }
    }
}
