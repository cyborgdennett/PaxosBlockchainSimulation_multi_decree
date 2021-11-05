using System;
using PaxosBlockchainSimulation.NodeAgents;
using PaxosBlockchainSimulation.Sensor_data;

namespace PaxosBlockchainSimulation
{
    class Program
    {
        static void Main(string[] args)
        {
            bool test = false;
    
            Node node = null;
            PortChat portChat = null;

            try
            {
                if (!test)
                {
                    portChat = new PortChat();
                    portChat.Start();
                }
               
                node = new Node(portChat, test);
            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
            }
           

        }
    }
}
