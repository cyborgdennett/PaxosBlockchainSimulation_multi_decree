using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using PaxosBlockchainSimulation.ClientServer;
using PaxosBlockchainSimulation.Messaging;
using Microsoft.EntityFrameworkCore;
using PaxosBlockchainSimulation.Database;
using PaxosBlockchainSimulation.Sensor_data;

namespace PaxosBlockchainSimulation.NodeAgents
{
    /// <summary>
    /// As written in THe Part-Time Parliament, a node requires 3 types of statuses to operate.
    /// </summary>
    public enum NodeStatus
    {
        idle,
        trying,
        polling
    }

    /// <summary>
    /// A node is the central component of the application. 
    /// A node contains all of the other components, such as client, server, proposer, acceptor and learner.
    /// Besides, the node usually contains network information such as its own endpoint, and all of the other known peers.
    /// </summary>
    public class Node
    {
        private bool canExecute = true;
        private static readonly string NODES_FILE_PATH = "nodes.csv";

        public int Id { get; private set; }
        public IPAddress IPAddress { get; private set; }
        public int PortNumber { get; private set; }
        public IPEndPoint EndPoint { get; private set; }
        public bool IsOnline { get; set; }
        public DateTime LastMessageReceivedAt { get; set; }
        public Client Client { get; private set; }
        public Server Server { get; private set; }
        public UdpClient Socket { get; private set; }
        public Cluster AllNodes { get; private set; }
        public Cluster Peers { get; private set; }
        public Cluster OnlinePeers { get; private set; }

        public Proposer Proposer { get; private set; }
        public Acceptor Acceptor { get; private set; }
        public Learner Learner { get; private set; }
        public Node PresidentNode;
        public PortChat senosr_data { get; set; }
        public Boolean testBool { get; set; }


        //ledger (solid) variables
        public decimal lastTried;
        public decimal prevBal;
        public byte[] prevDec;
        public decimal nextBal;

        //note (temporary) variables
        public NodeStatus status = NodeStatus.idle;
        public List<LastVote> prevVotes;
        public Cluster quorum;
        public Cluster voters;
        public byte[] decree;

        //temporary values introduced with the multi-decree parliament
        public bool isPresident;
        public bool isFill;
        public bool isNewDecree;
        public long entryId;

        //keeping track of time
        public static readonly int MINUTE_IN_PAXOS_TIME = 46; //see thesis why I chose this value (45.45ms)

        /// <summary>
        /// Node construtor for external nodes.
        /// That is, nodes which are not these. These will store connectivity information and other exceptionalities.
        /// </summary>
        /// <param name="id">Node id</param>
        /// <param name="ip">IP(v4) address</param>
        /// <param name="port">Port</param>
        public Node(int id, IPAddress ip, int port)
        {
            Id = id;
            IPAddress = ip;
            PortNumber = port;
            EndPoint = new IPEndPoint(ip, port);
            IsOnline = false;
            LastMessageReceivedAt = DateTime.MinValue;
        }

        /// <summary>
        ///  Constructor for self (this node, the current instance)
        /// </summary>
        public Node(PortChat a, Boolean test)
        {
            testBool = test; 
            senosr_data = a;
            PrepareDB(); //DISABLE WHEN NOT NEEDED. SQLite needs this.
            GetConnectionInformation();
            if (canExecute)
            {
                InitRoles();
                GetLedgerVariables();
                ConnectToNetwork().Wait();
            }
        }

        /// <summary>
        /// Prepares the database for Write-Ahead Logging
        /// DISABLE WHEN NOT NEEDED (SQLite requires WAL)
        /// </summary>
        private void PrepareDB()
        {
            Console.WriteLine("Preparing DB.");

            using (Ledger ledger = new Ledger())
            {
                Console.WriteLine("Connecting with DB.");
                var connection = ledger.Database.GetDbConnection();
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    Console.WriteLine("Opening DB in WAL.");
                    command.CommandText = "PRAGMA journal_mode=WAL;";
                    command.ExecuteNonQuery();
                }
            }
            Console.WriteLine("DB setup successful.");
        }

        /// <summary>
        /// Collects all connection information of this, and all external nodes of the network.
        /// Currently, the information is just saved in the text file.
        /// The own node is identified by comparing its own local IP address with the IP address in the nodes.csv file.
        /// The node currently allows multiple instances to be run on the same machine, for testing purposes.
        /// Connection is limited to LAN only, as of now.
        /// </summary>
        private void GetConnectionInformation()
        {
            try
            {
                IPAddress = GetLocalActiveIpAddress();

                if (canExecute)
                {
                    Console.WriteLine("Own IPv4: {0}", IPAddress);

                    Peers = new Cluster();
                    string[] endpoints = File.ReadAllLines(NODES_FILE_PATH);
                    bool foundSelf = false;
                    bool skippedFirst = false;

                    if (endpoints.Length <= 1)
                    {
                        canExecute = false;
                        Console.WriteLine("Couldn't find nodes to connect with.\nPlease specifiy nodes in the {0} file.", NODES_FILE_PATH);
                        return;
                    }

                    foreach (string endpoint in endpoints)
                    {
                        if (!skippedFirst)
                        {
                            skippedFirst = true;
                            continue;
                        }

                        string[] endpointPropterties = endpoint.Split(',');
                        int e_id = Int32.Parse(endpointPropterties[0]);
                        IPAddress e_ip = IPAddress.Parse(endpointPropterties[1]);
                        int e_port = Int32.Parse(endpointPropterties[2]);
                        Node e_node = new Node(e_id, e_ip, e_port);

                        if (e_ip.ToString() == IPAddress.ToString() && !foundSelf)
                        {
                            bool e_portInUse = CheckPortInUse(e_port);
                            if (!e_portInUse)
                            {
                                Console.WriteLine("Found self in list of endpoints.");
                                foundSelf = true;
                                Id = e_id;
                                IPAddress = e_ip;
                                PortNumber = e_port;
                                EndPoint = new IPEndPoint(e_ip, e_port);
                                IsOnline = true;
                                Socket = new UdpClient(EndPoint);
                                Console.WriteLine(ToString());
                                continue;
                            }else
                            {
                                Console.WriteLine("Port {0} already in use.", e_port);
                            }
                        }
                        Peers.TryAdd(e_id, e_node);
                    }

                    if (foundSelf == false)
                    {
                        Console.WriteLine("Not in list of nodes. Please specifiy your id, IP, and port in {0}.", NODES_FILE_PATH);
                        canExecute = false;
                        return;
                    }
                    else
                    {
                        Peers.PrintAllNodes();
                    }

                    UpdateOnlineNodes();
                    AllNodes = new Cluster(Peers);
                    AllNodes.TryAdd(Id, this);
                }
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("Peer file couldn't be found.");
                canExecute = false;
            }
            catch (NullReferenceException)
            {
                Console.WriteLine("Most likely local IPv4 couldn't be found.");
                canExecute = false;
            }
        }

        /// <summary>
        /// Gets the local active ip address. So if on wired connection, it will get the wired IPv4. 
        /// If the computer is on wireless, vice versa.
        /// </summary>
        /// <returns>The LAN IPv4 address</returns>
        private IPAddress GetLocalActiveIpAddress()
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Wireless80211 &&
                    (networkInterface.NetworkInterfaceType != NetworkInterfaceType.Ethernet || networkInterface.OperationalStatus != OperationalStatus.Up))
                {
                    continue;
                }

                foreach (var uniIpAddrInfo in networkInterface.GetIPProperties().UnicastAddresses.Where(x => networkInterface.GetIPProperties().GatewayAddresses.Count > 0))
                {
                    if (uniIpAddrInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                        return IPAddress.Parse(uniIpAddrInfo.Address.ToString());
                }
            }
            Console.WriteLine("Local IPv4 couldn't be found in list of authorized nodes.");
            canExecute = false;
            return null;
        }

        /// <summary>
        /// The code that will start the client and server of the node. These allow the node to connect to all other nodes.
        /// </summary>
        public async Task ConnectToNetwork()
        {
            Client = new Client(this);
            Server = new Server(this);
            await Proposer.BeginProposingOnInput();
        }

        /// <summary>
        /// Checks if the specified port is in use
        /// </summary>
        /// <param name="port">The port number to check</param>
        /// <returns>true = port in use; false = port is not in use</returns>
        private bool CheckPortInUse(int port)
        {
            return IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveUdpListeners()
                .Where(u => u.Port == port).Any() ? true : false;
        }

        public void UpdateOnlineNodes()
        {
            if (Peers != null || Peers.Count > 0)
            {
                OnlinePeers = Peers.GetOnlineNodes();
            }
        }

        /// <summary>
        /// Decides what role the node should have.
        /// THIS WAY OF SELECTING ROLES DEPENDS ON THE IMPLEMENTATION (see paxos simple, it's also not stated clearly in the part-time parliament)
        /// For now, we assume at least every node has every kind of agent, but can only propose new values if they're the president.
        /// </summary>
        private void InitRoles()
        {
            Proposer = new Proposer(this);
            Acceptor = new Acceptor(this);
            Learner = new Learner(this);
            isPresident = false;
        }

        /// <summary>
        /// Get the important Paxos variables from the database, and load them into variables.
        /// </summary>
        public void GetLedgerVariables()
        {
            List<LedgerEntry> entries = new List<LedgerEntry>();

            using (Ledger ledger = new Ledger())
            {
                lastTried = ledger.Progress.First().LastTried;
                prevBal = ledger.Progress.First().PrevBal;
                prevDec = ledger.Progress.First().PrevDec;
                nextBal = ledger.Progress.First().NextBal;
                entries = ledger.Entries.ToList();
            }

            PrintWrittenLedgerValues(entries);
        }

        public void PrintWrittenLedgerValues(List<LedgerEntry> entries)
        {
            Console.WriteLine();
            Console.WriteLine("====================Progress========================");
            Console.WriteLine("LastTried = {0}", lastTried);
            Console.WriteLine("PrevBal = {0}", prevBal);
            Console.WriteLine("PrevDec = {0}", MessageHelper.ByteArrayToString(prevDec));
            Console.WriteLine("NextBal = {0}", nextBal);
            Console.WriteLine("====================Decrees=========================");
            foreach (LedgerEntry entry in entries)
            {
                Console.WriteLine("Id={0}, decree={1}", entry.Id, entry.Decree);
            }
            Console.WriteLine("====================================================");
        }

        public override string ToString()
        {
            return String.Format("Id:\t{0}\nIP:\t{1}\nPort:\t{2}", Id, IPAddress, PortNumber);
        }
    }
}
