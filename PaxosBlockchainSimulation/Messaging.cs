using System;
using System.Text;
using PaxosBlockchainSimulation.NodeAgents;
using System.Globalization;

namespace PaxosBlockchainSimulation.Messaging
{
    /// <summary>
    /// Contains code for the preparing and decoding of messages sent between nodes.
    /// Messages are made as a Message concrete class, then encoded to a byte array for sending.
    /// When receiving such as message, a message is decoded to a concrete Message class.
    /// </summary>
    public static class MessageHelper
    {

        /// <summary>
        /// Fulfills requirement B1 of The Part-Time Parliament.
        /// Creates a messageId by glueing a message id to a node id.
        /// </summary>
        /// <param name="messageId">The message id to be sent by node</param>
        /// <param name="nodeId">The node id</param>
        /// <returns>A unique message id, e.g. (3.1.) where 3=messageId and 1=nodeId</returns>
        public static decimal CreateUniqueMessageId(long messageId, int nodeId)
        {
            return Convert.ToDecimal(String.Format("{0}.{1}", messageId, nodeId), CultureInfo.InvariantCulture);
        }

        public static String ByteArrayToString(byte[] bytesToParse)
        {
            return Encoding.ASCII.GetString(bytesToParse);
        }

        public static byte[] StringToByteArray(string str)
        {
            return Encoding.ASCII.GetBytes(str);
        }

        /// <summary>
        ///  Decodes a byte array to a message, if possible.
        ///  A typical message (e.g. a heartbeat) looks like: 1.1,HB;2.1,1 
        ///  where data before ';' is generic message information: message Id and message Type
        ///  and data after ';' is data specific for the type of message
        /// </summary>
        /// <param name="bytes">The bytes to parse to a message.</param>
        /// <returns>A message</returns>
        public static Message ByteArrayToMessage(byte[] bytes)
        {
            try
            {
                // Console.WriteLine("[{0}]", ByteArrayToString(bytes));
                // Console.WriteLine("[Amount of bytes: {0}]", bytes.Length);

                //separate all the information of the message
                string[] splitMessage = ByteArrayToString(bytes).Split(';');

                //generic information for every kind of message
                string[] messageInformation = splitMessage[0].Split(',');
                string[] fullMessageSplit = messageInformation[0].Split('.');
                long messageId = long.Parse(fullMessageSplit[0]);
                int senderId = Int32.Parse(fullMessageSplit[1]);
                string messageType = messageInformation[1];

                //the content specialised for the type of message
                string[] messageContent = splitMessage[1].Split(',');

                switch (messageType)
                {
                    case "ACM":
                        {
                            decimal replyToMessageId = Decimal.Parse(messageContent[0], CultureInfo.InvariantCulture);
                            return new ArrivalConfirmationMessage(messageId, replyToMessageId, senderId);
                        }
                    case "HB":
                        {
                            decimal lastTried = Decimal.Parse(messageContent[0], CultureInfo.InvariantCulture);
                            int isPredident = Int32.Parse(messageContent[1]);
                            return new Heartbeat(messageId, senderId, lastTried, isPredident);
                        }
                    case "NB":
                        {
                            decimal ballotId = Decimal.Parse(messageContent[0], CultureInfo.InvariantCulture);
                            long hasDecreesUntil = long.Parse(messageContent[1]);
                            return new NextBallot(messageId, senderId, ballotId, hasDecreesUntil);
                        }
                    case "LV":
                        {
                            decimal nextBalId = Decimal.Parse(messageContent[0], CultureInfo.InvariantCulture);
                            decimal previousBallotId = Decimal.Parse(messageContent[1], CultureInfo.InvariantCulture);
                            byte[] previousDecree = StringToByteArray(messageContent[2]);
                            string missingDecrees = messageContent[3];
                            return new LastVote(messageId, nextBalId, senderId, previousBallotId, previousDecree, missingDecrees);
                        }
                    case "BB":
                        {
                            decimal ballotId = Decimal.Parse(messageContent[0], CultureInfo.InvariantCulture);
                            byte[] decree = StringToByteArray(messageContent[1]);
                            return new BeginBallot(messageId, senderId, ballotId, decree);
                        }
                    case "VD":
                        {
                            decimal ballotId = Decimal.Parse(messageContent[0], CultureInfo.InvariantCulture);
                            return new Voted(messageId, senderId, ballotId);
                        }
                    case "SS":
                        {
                            byte[] decree = StringToByteArray(messageContent[0]);
                            long decreeId = long.Parse(messageContent[1]);
                            return new Success(messageId, senderId, decree, decreeId);
                        }
                    case "UBN":
                        {
                            decimal nextBal = Decimal.Parse(messageContent[0], CultureInfo.InvariantCulture);
                            return new UpdateBallotNumber(messageId, senderId, nextBal);
                        }
                    case "DP":
                        {
                            byte[] decree = StringToByteArray(messageContent[0]);
                            return new DecreeProposal(messageId, senderId, decree);
                        }
                    case "RME":
                        {
                            long decreeId = long.Parse(messageContent[0]);
                            string hasDecreesWritten = messageContent[1];
                            return new RequestMissingEntriesMessage(messageId, senderId, decreeId, hasDecreesWritten);
                        }
                    case "IME":
                        {
                            string missingEntries = messageContent[0];
                            return new InformMissingEntriesMessage(messageId, senderId, missingEntries);
                        }
                    default:
                        {
                            Console.WriteLine("Unknown message type.");
                            return new Message();
                        }
                }
            }
            catch(Exception e)
            {
                Console.WriteLine("Failed parsing message.");
                Console.WriteLine(e.StackTrace);
                return new Message();
            }
        }

    }

    /// <summary>
    /// Contains generic information which every kind of concrete message class should contain.
    /// </summary>
    public class Message
    {
        public decimal _id { get; protected set; }
        public int _senderId { get; protected set; }
        public bool _doResend { get; protected set; }

        public Message() { }

        public virtual byte[] ToByteArray()
        {
            return new byte[] { };
        }
    }

    /// <summary>
    /// A message to confirm the arrival of a data message.
    /// </summary>
    public class ArrivalConfirmationMessage : Message
    {
        public decimal _replyToMessageId;

        public ArrivalConfirmationMessage(long messageIdCounter, decimal replyToMessageId, int senderId)
        {
            _doResend = false;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _replyToMessageId = replyToMessageId;
            _senderId = senderId;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},ACM;{1}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _replyToMessageId));
        }
    }

    /// <summary>
    /// A message sent by a node to each peer, to check the online status.
    /// </summary>
    public class Heartbeat : Message
    {
        public int _isPresident;
        public decimal _lastTried;

        //for sending
        public Heartbeat(Node node, long messageIdCounter)
        {
            _doResend = false;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, node.Id);
            _lastTried = node.lastTried;
            _isPresident = Convert.ToInt32(node.isPresident);
        }

        //for receiving/parsing
        public Heartbeat(long messageIdCounter, int senderId, decimal lastTried, int isPresident)
        {
            _doResend = false;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;
            _lastTried = lastTried;
            _isPresident = isPresident;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},HB;{1},{2}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _lastTried.ToString(CultureInfo.InvariantCulture),
                                                                 _isPresident
                                                                 ));
        }
    }


    public class NextBallot : Message
    {
        public decimal _ballotId;
        public long _hasDecreesUntil;

        public NextBallot(long messageIdCounter, int senderId, decimal ballotId, long hasDecreesUntil)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _ballotId = ballotId;
            _hasDecreesUntil = hasDecreesUntil;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},NB;{1},{2}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _ballotId.ToString(CultureInfo.InvariantCulture),
                                                                 _hasDecreesUntil));
        }
    }


    public class LastVote : Message
    {
        public decimal _nextBal, _prevBal;
        public byte[] _prevDecree;
        public string _missingDecrees;

        public LastVote(long messageIdCounter, decimal nextBal, int senderId, decimal prevBal, byte[] prevDecree, string missingDecrees)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _nextBal = nextBal;
            _prevBal = prevBal;
            _prevDecree = prevDecree;
            _missingDecrees = missingDecrees;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},LV;{1},{2},{3},{4}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _nextBal.ToString(CultureInfo.InvariantCulture),
                                                                 _prevBal.ToString(CultureInfo.InvariantCulture),
                                                                 MessageHelper.ByteArrayToString(_prevDecree),
                                                                 _missingDecrees));
        }

        public override string ToString()
        {
            return String.Format("\nNextBal={0}\nNode={1}\nPrevBal={2}\nPrevDecree={3}\nMissingDecreeString={4}",
                                 _nextBal.ToString(CultureInfo.InvariantCulture),
                                 _senderId,
                                 _prevBal.ToString(CultureInfo.InvariantCulture),
                                 MessageHelper.ByteArrayToString(_prevDecree),
                                 _missingDecrees);
        }
    }

    public class BeginBallot : Message
    {
        public decimal _ballotId;
        public byte[] _decree;

        public BeginBallot(long messageIdCounter, int senderId, decimal ballotId, byte[] decree)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _ballotId = ballotId;
            _decree = decree;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},BB;{1},{2}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _ballotId.ToString(CultureInfo.InvariantCulture),
                                                                 MessageHelper.ByteArrayToString(_decree)));
        }
    }

    public class Voted : Message
    {
        public decimal _ballotId;

        public Voted(long messageIdCounter, int senderId, decimal ballotId)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _ballotId = ballotId;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},VD;{1}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _ballotId.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public class Success : Message
    {
        public decimal _ballotId;
        public byte[] _decree;
        public long _decreeId;

        public Success(long messageIdCounter, int senderId, byte[] decree, long decreeId)
        {
            _doResend = true;
            _senderId = senderId;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);

            _decree = decree;
            _decreeId = decreeId;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},SS;{1},{2}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 MessageHelper.ByteArrayToString(_decree),
                                                                 _decreeId));
        }
    }

    public class UpdateBallotNumber : Message
    {
        public decimal _nextBal;

        public UpdateBallotNumber(long messageIdCounter, int senderId, decimal nextBal)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _nextBal = nextBal;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},UBN;{1}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _nextBal.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public class DecreeProposal : Message
    {
        public byte[] _decree { get; private set; }

        public DecreeProposal(long messageIdCounter, int senderId, byte[] decree)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _decree = decree;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},DP;{1}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 MessageHelper.ByteArrayToString(_decree)));
        }
    }

    public class RequestMissingEntriesMessage: Message
    {
        public long _decreeId { get; private set; }
        public string _entriesInOwnLedgerString;

        public RequestMissingEntriesMessage(long messageIdCounter, int senderId, long decreeId, string entriesInOwnLedgerString)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _decreeId = decreeId;
            _entriesInOwnLedgerString = entriesInOwnLedgerString;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},RME;{1},{2}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _decreeId,
                                                                 _entriesInOwnLedgerString));
        }
    }

    public class InformMissingEntriesMessage: Message
    {
        public string _entriesString { get; private set; }

        public InformMissingEntriesMessage(long messageIdCounter, int senderId, string entriesString)
        {
            _doResend = true;
            _id = MessageHelper.CreateUniqueMessageId(messageIdCounter, senderId);
            _senderId = senderId;

            _entriesString = entriesString;
        }

        public override byte[] ToByteArray()
        {
            return MessageHelper.StringToByteArray(String.Format("{0},IME;{1}",
                                                                 _id.ToString(CultureInfo.InvariantCulture),
                                                                 _entriesString));
        }
    }

}
