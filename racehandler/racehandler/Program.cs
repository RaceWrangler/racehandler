using System;

namespace racehandler
{
    class Program
    {
        static void Main(string[] args)
        {
            DeadDropManager ddm = new DeadDropManager();

            ddm.AddDeadDrop("racespy");

            Console.WriteLine("Press 'q' to quit...");
            while (Console.Read() != 'q') ;

            ddm.BugOut();
        }
    }
}
