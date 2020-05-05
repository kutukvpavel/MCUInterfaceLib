using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.IO.Ports;
using MCUI;

namespace ExampleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    Console.WriteLine("No port specified, please enter port name: COM32");
                    args = new string[] { /*Console.ReadLine()*/ "COM32" };
                }
                SwitchController d = new SwitchController() { PassResponse = ResponseReaction };
                d.Port.Open(args[0]);
                Console.WriteLine("Press ENTER to check device presence or ANY other key to abort operation...");
                if (Console.ReadKey(true).Key == ConsoleKey.Enter)
                {
                    d.DetectPresence();
                    Console.WriteLine("Waiting for answer... Expected response is: " + Encoding.ASCII.GetString(d.PresenceAnsw));
                }
                else
                {
                    return;
                }
                for (int i = 0; (i < 10) && !d.Present; i++)
                {
                    Thread.Sleep(75);
                }
                Console.WriteLine(d.Present ? "Device present." : 
                    ("Timeout elapsed. Current buffer: " + Encoding.ASCII.GetString(d.BufferedData)));
                Console.WriteLine("Waiting for further responses... Press any key to exit...");
                Console.ReadKey();
                d.Port.Close();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

        }
        static void ResponseReaction(string txt)
        {
            Console.WriteLine("Response received: " + txt);
        }
    }

    public class SwitchController : SimpleDevice
    {
        public SwitchController() : base(Encoding.ASCII.GetBytes("\r\n"), Encoding.ASCII.GetBytes(":"), Encoding.ASCII.GetBytes("P"),
            Encoding.ASCII.GetBytes("P:SwitchController"), 9600, 8, Parity.None, StopBits.One, Handshake.None)
        {

        }
        public delegate void ResponseRoutine(string text);
        public ResponseRoutine PassResponse; 
        enum Commands : byte
        {
            Presence = (byte)'P',
            Inversion = (byte)'I',
            Output = (byte)'O',
            Mapping = (byte)'M',
            NormalState = (byte)'N',
            ManualOverride = (byte)'V',
            Read = (byte)'R'
        }
        enum Arguments : byte
        {
            ON = (byte)'N',
            OFF = (byte)'F',
            Actual = (byte)'A',
            EEPROM = (byte)'E',
            Outputs = (byte)'O',
            Inputs = (byte)'I'
        }
        enum Alarms : byte
        {
            Active = (byte)'A',
            Seized = (byte)'S'
        }
        static readonly string[] AlarmsText = { "Open: ", "Closed: " };
        static readonly byte ErrorDesignator = (byte)'E';

        public override void CommandReceived(byte[] cmd, byte[][] args)
        {
            if (PassResponse == null) return;
            string txt = "";
            if (ResponseExpected)
            {
                if (args.Length > 1)
                {
                    if ((args[0].Length == 1) && (args[0][0] == ErrorDesignator))
                    {
                        txt = "Error: ";
                    }
                }
                txt += Encoding.ASCII.GetString(args[args.Length - 1]);
            }
            else
            {
                txt = Encoding.ASCII.GetString(args[0]);
                switch (cmd[0])
                {
                    case (byte)Alarms.Active:
                        txt = AlarmsText[0] + txt;
                        break;
                    case (byte)Alarms.Seized:
                        txt = AlarmsText[1] + txt;
                        break;
                    default:
                        txt = "Unexpected response.";
                        break;
                }
            }
            PassResponse(txt);
        }

        public override void Terminal(string response)
        {
            throw new NotImplementedException();
        }
    }
}
