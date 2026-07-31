using System;
using System.Threading;

namespace RobloxKeeper
{
    // Queue-waits on the Roblox singleton mutex from a dedicated thread, the same
    // way Roblox clients do. The kernel hands over ownership the instant the
    // previous owner releases or dies, so a launching client can never win the
    // race against us. Polling can't guarantee that; a blocking wait can.
    class MutexKeeper
    {
        const string ROBLOX_MUTEX = "ROBLOX_singletonMutex";

        Thread worker;
        ManualResetEvent stop;
        public volatile bool Held;

        public bool Running { get { return worker != null && worker.IsAlive; } }

        public void Start()
        {
            if (Running) return;
            stop = new ManualResetEvent(false);
            Held = false;
            worker = new Thread(Run);
            worker.IsBackground = true;
            worker.Name = "MutexKeeper";
            worker.Start();
        }

        public void Stop()
        {
            if (!Running) { Held = false; return; }
            stop.Set();
            worker.Join(3000);
            worker = null;
            Held = false;
        }

        void Run()
        {
            Mutex m = null;
            try
            {
                bool createdNew;
                m = new Mutex(false, ROBLOX_MUTEX, out createdNew);
                int signaled;
                try { signaled = WaitHandle.WaitAny(new WaitHandle[] { stop, m }); }
                catch (AbandonedMutexException) { signaled = 1; }
                if (signaled == 0) return;   // disabled before acquisition
                Held = true;
                stop.WaitOne();              // own it until disabled
                try { m.ReleaseMutex(); } catch { }
            }
            catch { }
            finally
            {
                Held = false;
                if (m != null) m.Close();
            }
        }
    }
}
