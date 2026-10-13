using System;

namespace BurgerEmpire.Core
{
    public enum RewardedAdSignal { Earned, Closed, Failed }
    public enum RewardedAdState { Ready, Unavailable, Showing, AwaitingReward, Rewarded, Canceled, Failed, TimedOut, GrantFailed }

    // Provider adapters must deliver callbacks on the game's main thread and retain
    // the callback for the specific show request (never route old events to a new one).
    public interface IRewardedAdProvider
    {
        bool IsReady { get; }
        void Show(Action<RewardedAdSignal> callback);
        void Dismiss();
    }

    public sealed class RewardedAdFlow : IDisposable
    {
        public const int BonusCoins=100;
        public const float ShowTimeout=120, CloseGrace=2;
        readonly IRewardedAdProvider provider;
        readonly Action<string,int> grant;
        int generation;
        bool disposed,earned,grantFailed;
        float elapsed;
        string request;
        public RewardedAdState State {get;private set;}
        public bool Busy => State==RewardedAdState.Showing||State==RewardedAdState.AwaitingReward;
        public bool CanBegin => !disposed&&!Busy&&provider.IsReady;
        public void RefreshAvailability(){if(!disposed&&!Busy)State=provider.IsReady?RewardedAdState.Ready:RewardedAdState.Unavailable;}

        // The grant sink owns persistence. This flow prevents repeated callbacks within
        // one process; durable receipt recovery must be implemented by a live adapter.
        public RewardedAdFlow(IRewardedAdProvider provider,Action<string,int> grant)
        {
            this.provider=provider??throw new ArgumentNullException(nameof(provider));
            this.grant=grant??throw new ArgumentNullException(nameof(grant));
            State=provider.IsReady?RewardedAdState.Ready:RewardedAdState.Unavailable;
        }
        public bool Begin()
        {
            if(disposed||Busy)return false;
            if(!provider.IsReady){State=RewardedAdState.Unavailable;return false;}
            int current=++generation;request=Guid.NewGuid().ToString("N");
            earned=grantFailed=false;elapsed=0;State=RewardedAdState.Showing;
            try {provider.Show(signal=>Receive(current,signal));}
            catch {if(current==generation&&Busy)Finish(RewardedAdState.Failed);}
            return true;
        }
        void Receive(int current,RewardedAdSignal signal)
        {
            if(disposed||current!=generation||!Busy)return;
            switch(signal) {
                case RewardedAdSignal.Earned:
                    if(earned)return;
                    earned=true; // Set before the sink: duplicate or reentrant signals cannot grant twice.
                    try {grant(request,BonusCoins);}catch {grantFailed=true;}
                    if(State==RewardedAdState.AwaitingReward)Finish(RewardedAdState.Rewarded);
                    break;
                case RewardedAdSignal.Closed:
                    if(earned)Finish(RewardedAdState.Rewarded);
                    else if(State!=RewardedAdState.AwaitingReward){State=RewardedAdState.AwaitingReward;elapsed=0;}
                    break;
                case RewardedAdSignal.Failed: Finish(RewardedAdState.Failed);break;
            }
        }
        public void Tick(float seconds)
        {
            if(!Busy||seconds<=0||float.IsNaN(seconds)||float.IsInfinity(seconds))return;
            elapsed+=seconds;
            if(State==RewardedAdState.AwaitingReward&&elapsed>=CloseGrace)Finish(RewardedAdState.Canceled);
            else if(State==RewardedAdState.Showing&&elapsed>=ShowTimeout)Finish(RewardedAdState.TimedOut);
        }
        void Finish(RewardedAdState fallback)
        {
            State=grantFailed?RewardedAdState.GrantFailed:earned?RewardedAdState.Rewarded:fallback;
            ++generation; // Invalidate callbacks before dismissing a provider that may call back synchronously.
            try {provider.Dismiss();}catch { /* Teardown cannot manufacture an earned reward. */ }
        }
        public void Cancel(){if(Busy)Finish(RewardedAdState.Canceled);}
        public void Dispose(){if(disposed)return;Cancel();disposed=true;++generation;}
    }
}
