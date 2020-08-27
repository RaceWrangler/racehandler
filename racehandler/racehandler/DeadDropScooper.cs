// This class recovers images from the dead drop locations on each spy

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace racehandler
{
    class DeadDropScooper
    {
        public bool Terminate { get; set; }
        private string spyName;
        public DeadDropScooper(string loc)
        {
            spyName = loc;
        }

        public void Run()
        {
            if (!Directory.Exists("\\AuditImages"))
            {
                Directory.CreateDirectory("\\AuditImages");
            }
            if(!Directory.Exists($"\\AuditImages\\{spyName}"))
            {
                Directory.CreateDirectory($"\\AuditImages\\{spyName}");
            }

            string watchDir = $"\\\\{spyName}\\DeadDrop";
            while (!Terminate)
            {
                if (Directory.Exists(watchDir))
                {
                    foreach (var img in Directory.EnumerateFiles(watchDir))
                    {
                        Scoop(spyName, img);
                    }
                }
                Thread.Sleep(1000);
            }
            Console.WriteLine($"No longer monitoring the dead drop for {spyName}!");
        }

        private void Scoop(string spyName, string img)
        {
            Console.WriteLine($"New file: {img}");

            string imgName = img.Split("\\").Last();

            File.Move(img, $"\\AuditImages\\{spyName}\\{imgName}", true);
        }
    }
}
