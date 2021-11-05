
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Linq;
using System.Collections.Generic;

namespace PaxosBlockchainSimulation.NodeAgents
{
    /// <summary>
    ///   A cluster is very simply a collection of nodes.
    ///   The nodes are saved in a dict to easily find a node by id.
    /// </summary>
    public class Cluster : ConcurrentDictionary<int, Node>
    {
        public Cluster()
        {
        }

        public Cluster(List<Node> peers)
        {
            foreach(var peer in peers)
            {
                TryAdd(peer.Id, peer);
            }
        }


        public Cluster(Cluster otherCluster)
        {
            foreach(var node in otherCluster.Values)
            {
                TryAdd(node.Id, node);
            }
        }

        public void PrintAllNodes()
        {
            Console.WriteLine("\n===========Other nodes in network===============");
            foreach (var node in Values)
            {
                Console.WriteLine(node.ToString());
            }
            Console.WriteLine("================================================");
        }

        public Node GetNodeById(int id)
        {
            if(ContainsKey(id))
                return this[id];
            else
                return new Node(Int32.MinValue, IPAddress.Parse("127.0.0.1"), 0); //No such node exists
        }

        public Cluster GetOnlineNodes()
        {
            List<Node> onlineNodeList = Values.Where(n => n.IsOnline).ToList();
            return new Cluster(onlineNodeList);
        }


        /// <summary>
        /// One of the important requirements of the parliament was that a quorum consists of a majority of nodes.
        /// This methods returns true if that is the case
        /// </summary>
        /// <param name="otherCluster">The cluster to check majority over</param>
        /// <returns>True = has a majority. False = no majority</returns>
        public bool HasMajorityOf(Cluster otherCluster)
        {
            var elementsInBoth = this.Keys.Intersect(otherCluster.Keys).ToList();
            return elementsInBoth.Count > (otherCluster.Count / 2);
        }

        public Cluster GetClusterExcludingNode(Node node)
        {
            Cluster tempCluster = new Cluster(this);
            Node removedNode;
            tempCluster.TryRemove(node.Id, out removedNode);
            return tempCluster;
        }
    }
}
