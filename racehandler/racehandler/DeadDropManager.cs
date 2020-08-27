// This class keeps track of the existing dead drops
// it also configures a scooper for each dead drop.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace racehandler
{
    class DeadDropManager
    {
        private Dictionary<DeadDropScooper, Thread> deadDropThreads;

        public DeadDropManager()
        {
            deadDropThreads = new Dictionary<DeadDropScooper, Thread>();
        }
        
        public void AddDeadDrop(string loc)
        {
            DeadDropScooper dds = new DeadDropScooper(loc);

            var t = new Thread(new ThreadStart(dds.Run));

            t.Start();

            deadDropThreads.Add(dds, t);
        }


        public void AbandonDeadDrop(string loc)
        {
            //if(DeadDrops.Contains(loc))
            //{

            //}
        }

        public void BugOut()
        {
            foreach(var t in deadDropThreads)
            {
                t.Key.Terminate = true;
            }
        }
    }
}
