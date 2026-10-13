using System;
using System.IO;
using System.Text;

namespace BurgerEmpire.Core
{
    public class SaveFiles
    {
        public virtual bool Exists(string path){try{File.GetAttributes(path);return true;}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}}
        public virtual string Read(string path){if(new FileInfo(path).Length>SaveJson.MaxLength)throw new FormatException("Oversized save");return File.ReadAllText(path,new UTF8Encoding(false,true));}
        public virtual void Write(string path,string value){using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None)){var bytes=new UTF8Encoding(false,true).GetBytes(value);stream.Write(bytes,0,bytes.Length);stream.Flush(true);}}
        public virtual void Replace(string from,string to,string backup){File.Replace(from,to,backup);}
        public virtual void Move(string from,string to){File.Move(from,to);}
        public virtual void Copy(string from,string to){File.Copy(from,to,true);}
        public virtual void Delete(string path){File.Delete(path);}
    }
    public sealed class ProgressStore
    {
        readonly string path;readonly Func<string,Progress> deserialize;readonly Func<Progress,string> serialize;readonly SaveFiles files;
        public bool Blocked {get;private set;}
        public string State {get;private set;}="new";
        public string Recovery {get;private set;}="";
        public string LastError {get;private set;}="";
        public DateTime? LastSaved {get;private set;}
        public string Path {get{return path;}}
        sealed class Candidate {public string path,raw,state="missing";public Progress progress;}
        public ProgressStore(string path,Func<string,Progress> deserialize,Func<Progress,string> serialize,SaveFiles files=null)
        {this.path=path;this.deserialize=deserialize;this.serialize=serialize;this.files=files??new SaveFiles();}
        Progress Decode(string value){SaveJson.Validate(value);var p=deserialize(value);if(p==null)throw new FormatException("Empty progress");return new Simulation(p).P;}
        Candidate Read(string source)
        {
            var c=new Candidate{path=source};try{if(!files.Exists(source))return c;c.raw=files.Read(source);c.progress=Decode(c.raw);c.state="valid";}
            catch(FutureSaveException){c.state="future";}catch(IOException){c.state="unreadable";}catch(UnauthorizedAccessException){c.state="unreadable";}catch(Exception){c.state="invalid";}return c;
        }
        public Progress Load()
        {
            Blocked=false;LastError="";Recovery="";LastSaved=null;
            var main=Read(path);var pending=Read(path+".tmp");var backup=Read(path+".bak");
            foreach(var c in new[]{main,pending,backup})if(c.state=="future"||c.state=="unreadable"){Blocked=true;State=c.state;return null;}
            var chosen=main.state=="valid"?main:pending.state=="valid"?pending:backup.state=="valid"?backup:null;
            if(chosen!=null){State=chosen==main?"loaded":"recovered";Recovery=chosen==main?"":chosen==pending?"pending":"backup";return chosen.progress;}
            if(main.state=="missing"&&pending.state=="missing"&&backup.state=="missing"){State="new";return null;}
            Blocked=true;State="invalid";return null;
        }
        public bool Save(Progress progress)
        {
            if(Blocked)return false;
            try {
                string value=serialize(progress);Decode(value);
                var main=Read(path);var pending=Read(path+".tmp");var backup=Read(path+".bak");
                foreach(var c in new[]{main,pending,backup})if(c.state=="future"||c.state=="unreadable"){Blocked=true;State=c.state;return false;}
                // Preserve a recovered temporary file before reusing its name.
                if(main.state!="valid"&&pending.state=="valid"&&backup.state!="valid")files.Copy(path+".tmp",path+".bak");
                files.Write(path+".tmp",value);Decode(files.Read(path+".tmp"));
                if(main.state=="valid") {
                    try{files.Replace(path+".tmp",path,path+".bak");}
                    catch(NotSupportedException){FallbackCommit();}
                } else {
                    if(main.state!="missing")files.Move(path,path+".recovery-"+Guid.NewGuid().ToString("N")+".json");
                    files.Move(path+".tmp",path);
                }
                State="saved";LastSaved=DateTime.Now;LastError="";return true;
            }catch(Exception e){LastError=e.GetType().Name;State="write-error";return false;}
        }
        void FallbackCommit()
        {
            // Before removing either known-good generation, another flushed copy exists.
            files.Write(path+".bak.new",files.Read(path));
            if(files.Exists(path+".bak"))files.Delete(path+".bak");
            files.Move(path+".bak.new",path+".bak");
            files.Delete(path);files.Move(path+".tmp",path);
        }
    }
}
