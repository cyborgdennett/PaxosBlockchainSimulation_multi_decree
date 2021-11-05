using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using PaxosBlockchainSimulation.NodeAgents;
using PaxosBlockchainSimulation.Messaging;

namespace PaxosBlockchainSimulation.ClientServer
{
    /// <summary>
    /// The client is the 'sender' of the node; it can send messages to other nodes in the network.
    /// A periodic check (see MESSAGE_CONFIRMATION_CHECK_INTERVAL variable) for the UDP transport layer has been implemented.
    /// See the thesis for more information.
    /// </summary>
    public class Client
    {
        private Node parentNode;
        private UdpClient client;
        public bool IsSending { get; private set; }
        private Thread messageArrivalCheckThread;

        //Dictionary<messageId, Tuple<msgContent, List<recipientNodeId>>>
        private ConcurrentDictionary<decimal, Tuple<Message, Cluster>> unconfirmedMessages; 
        
        //Dictionary<messageId, retriesLeft>. Keeps track of how many times a message has to be resent until's not needed anymore.
        private ConcurrentDictionary<decimal, int> messageRetries; 

        public long _messageIdCounter { get; private set; }
        private static readonly int MAX_MESSAGE_ID = 10000;
        private static readonly int MESSAGE_CONFIRMATION_CHECK_INTERVAL = 300;
        private static readonly int MESSAGE_RETRIES = 3;

        public Client(Node node)
        {
            parentNode = node;
            client = parentNode.Socket;

            //message confirmation
            unconfirmedMessages = new ConcurrentDictionary<decimal, Tuple<Message, Cluster>>();
            messageRetries = new ConcurrentDictionary<decimal, int>();
            _messageIdCounter = 1;
            IsSending = true;
            InitMessageArrivalCheckThread();
        }

        /// <summary>
        /// Sends a single message to a node
        /// </summary>
        /// <param name="message">Message to send</param>
        /// <param name="node">Node to send the message to</param>
        /// <param name="doInCrementMsgId">Increment the message id counter for this message</param>
        /// <param name="doUnconfirm">This message needs to be confirmed for arrival</param>
        public async Task SendMessageToNode(Message message, Node node, bool doInCrementMsgId, bool doUnconfirm)
        {
            if (node != null)
            {
                if (message._doResend && doUnconfirm)
                    UnconfirmMessage(message, node);

                byte[] messageInBytes = message.ToByteArray();
                await client.SendAsync(messageInBytes, messageInBytes.Length, node.EndPoint);

                if (doInCrementMsgId)
                    IncrementMessageIdCounter();
            }
            else
            {
                Console.WriteLine("Tried to send a message to an unknown peer.");
            }
        }


        /// <summary>
        /// Sends a single message to a node
        /// </summary>
        /// <param name="message">Message to send</param>
        /// <param name="nodeId">Id of Node to send the message to</param>
        /// <param name="doInCrementMsgId">Increment the message id counter for this message</param>
        /// <param name="doUnconfirm">This message needs to be confirmed for arrival</param>
        public async Task SendMessageToNode(Message message, int nodeId, bool incrementMsgIdCounter, bool doUnconfirm)
        {
            Node node = null;
            parentNode.AllNodes.TryGetValue(nodeId, out node);

            if(node != null)
            {
                await SendMessageToNode(message, node, incrementMsgIdCounter, doUnconfirm);
            }
            else
            {
                Console.WriteLine("Tried sending a message to an unknown peer.");
            }
        }

        /// <summary>
        /// Sends a message to all peers in a cluster.
        /// </summary>
        /// <param name="data">The data to send.</param>
        /// <param name="cluster">The cluster (collection of peers) to send the data to.</param>
        /// <param name="doUnconfirm">This message needs to be confirmed for arrival</param>
        public async Task SendMessageToCluster(Message message, Cluster cluster, bool doUnconfirm)
        {
            foreach (Node peer in cluster.Values)
            {
                await SendMessageToNode(message, peer.Id, false, doUnconfirm);
            }
            IncrementMessageIdCounter();
        }

        /// <summary>
        /// Increases the id of the message sent
        /// </summary>
        private void IncrementMessageIdCounter()
        {
            if(_messageIdCounter >= MAX_MESSAGE_ID)
            {
                _messageIdCounter = 1;
            }else
            {
                _messageIdCounter++;
            }
        }


        /// <summary>
        /// Send a confirmation of arrival of a message
        /// </summary>
        /// <param name="message">The arrived message</param>
        /// <returns></returns>
        public async Task SendArrivalConfirmation(Message message)
        {
            ArrivalConfirmationMessage arrivalConfirmationMessage = new ArrivalConfirmationMessage(_messageIdCounter, message._id, parentNode.Id);
            await SendMessageToNode(arrivalConfirmationMessage, message._senderId, true, false);
        }

        /// <summary>
        /// Removes a recipient from the list of unconfirmed messages (unconfirmed by certain peers).
        /// </summary>
        /// <param name="arrivalConfirmationMessage"></param>
        public void ConfirmArrival(ArrivalConfirmationMessage arrivalConfirmationMessage)
        {
            decimal messageId = arrivalConfirmationMessage._replyToMessageId;

            //find all of the peers of which confirmation is needed
            Tuple<Message, Cluster> messageAndRecipients;
            unconfirmedMessages.TryGetValue(messageId, out messageAndRecipients);

            if (messageAndRecipients != null)
            {
                Node nodeToRemove;
                messageAndRecipients.Item2.TryRemove(arrivalConfirmationMessage._senderId, out nodeToRemove);
                if (messageAndRecipients.Item2.Count <= 0)
                {
                    unconfirmedMessages.TryRemove(messageId, out messageAndRecipients);
                }
            }
        }


        /// <summary>
        /// Adds a message to the dictionary of messages that have not yet been confirmed to have arrived
        /// </summary>
        /// <param name="message">Message which has not been confirmed to be received.</param>
        /// <param name="recipient">The node this message was sent to.</param>
        private void UnconfirmMessage(Message message, Node recipient)
        {
            if (!unconfirmedMessages.ContainsKey(message._id))
            {
                Cluster recipientList = new Cluster();
                recipientList.TryAdd(recipient.Id, recipient);
                Tuple<Message, Cluster> recipientTuple = new Tuple<Message, Cluster>(message, recipientList);
                unconfirmedMessages.TryAdd(message._id, recipientTuple);
                messageRetries.TryAdd(message._id, MESSAGE_RETRIES);
            }
            else
            {
                Tuple<Message, Cluster> messageAndRecipients;
                unconfirmedMessages.TryGetValue(message._id, out messageAndRecipients);

                if (messageAndRecipients != null)
                    messageAndRecipients.Item2.TryAdd(recipient.Id, recipient);
            }
        }

        /// <summary>
        /// Initialises and starts the thread which checks for unconfirmed messages. 
        /// The thread sends messages again if no confirmation has been received.
        /// </summary>
        private void InitMessageArrivalCheckThread()
        {
            messageArrivalCheckThread = new Thread(async () =>
                    {
                        while (IsSending)
                        {
                            await ResendUnconfirmedMessages();
                            messageArrivalCheckThread.Join(MESSAGE_CONFIRMATION_CHECK_INTERVAL);
                            StopResendingCheck();
                        }
                    });
            messageArrivalCheckThread.Start();
        }

        /// <summary>
        /// Resends all messages of whom no confirmation has been gotten.
        /// </summary>
        private async Task ResendUnconfirmedMessages()
        {
            if (unconfirmedMessages.Count > 0)
            {
                List<decimal> unconfirmedMessageIds = unconfirmedMessages.Keys.ToList();
                foreach (var unconfirmedMessageId in unconfirmedMessageIds)
                {
                    Tuple<Message, Cluster> messageAndRecipients;
                    unconfirmedMessages.TryGetValue(unconfirmedMessageId, out messageAndRecipients);

                    if (messageAndRecipients != null)
                    {
                        Message message = messageAndRecipients.Item1;
                        Cluster nodesToSendTo = messageAndRecipients.Item2;
                        await SendMessageToCluster(message, nodesToSendTo.GetOnlineNodes(), false);

                        int amountOfRetries = Int32.MinValue;
                        messageRetries.TryGetValue(unconfirmedMessageId, out amountOfRetries);
                        messageRetries.TryUpdate(unconfirmedMessageId, amountOfRetries - 1, amountOfRetries);
                    }
                }
            }
        }

        /// <summary>
        ///   Cleans up all the nodes still needing a certain message to be sent, 
        ///   after a certain amount of time and attempts of reconnection have been made
        /// </summary>
        private void StopResendingCheck()
        {
            if (messageRetries.Count > 0)
            {
                var messagesWithoutRetries = messageRetries.Where(m => m.Value == 0).ToList();
                foreach (var messageRetry in messagesWithoutRetries)
                {
                    Tuple<Message, Cluster> msgInfo;
                    unconfirmedMessages.TryRemove(messageRetry.Key, out msgInfo);
                    int retriesLeft;
                    messageRetries.TryRemove(messageRetry.Key, out retriesLeft);
                }
            }
        }

        /// <summary>
        /// A heartbeat is a message to tell the other nodes THIS NODE is online.
        /// </summary>
        /// <param name="cluster"></param>
        /// <returns></returns>
        public async Task SendHeartbeatToCluster(Cluster cluster)
        {
            Heartbeat heartbeat = new Heartbeat(parentNode, _messageIdCounter);
            await SendMessageToCluster(heartbeat, cluster, false);
        }
    }
}
