using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaxosBlockchainSimulation.Messaging;
using Microsoft.EntityFrameworkCore;
using PaxosBlockchainSimulation.Database;

namespace PaxosBlockchainSimulation.NodeAgents
{
    /// <summary>
    /// The Proposer is capable of conducting ballots.
    /// It knows when a voting round was successful, and will notify the other nodes when such is the case.
    /// </summary>
    public class Proposer
    {
        private Node _parentNode;
        private DateTime TimeAtPreviousAction;
        public static readonly string OLIVE_DAY_DECREE = "The ides of February is national olive day";
        public bool presidentInitTaskFinished;
        private Queue<byte[]> Proposals;
        private int totalQueeSize = 5;
        private Thread ExecutePaxosOnReceivedProposalThread;

        public Proposer(Node node)
        {
            _parentNode = node;
            Proposals = new Queue<byte[]>();
        }

        /// <summary>
        /// Allows for sending proposals from the command line.
        /// If the current node is NOT the president, redirect the proposal to the president
        /// </summary>
        public async Task BeginProposingOnInput()
        {

            Console.WriteLine("[Proposer] Preparing...");
            while (!_parentNode.Client.IsSending && !_parentNode.Server.isListening
                   || (_parentNode.isPresident && !presidentInitTaskFinished))
            {
                //If the current node is a president, wait for the inital president tasks to be finished
                Thread.Sleep(100);
            }

            
            while (_parentNode.Client.IsSending && _parentNode.Server.isListening)
            {

                Thread.Sleep(1000);
                string input = "";
                if (!_parentNode.testBool)
                {
                    input = _parentNode.senosr_data.Read().Trim();
                    if (input == "") continue;
                }
                else
                {
                   input = Console.ReadLine();
                }

               
                byte[] inputInBytes = MessageHelper.StringToByteArray(input);

                if(_parentNode.isPresident)
                {
                    Proposals.Enqueue(inputInBytes);
                }
                else if (!_parentNode.isPresident)
                {
                    if (_parentNode.PresidentNode == null)
                    {
                        Console.WriteLine("[Proposer] Waiting for president to be known...");
                        while (_parentNode.PresidentNode == null)
                        {
                            Thread.Sleep(100);
                        }
                    }

                    //send the proposal to the president
                    DecreeProposal decreeProposal = new DecreeProposal(_parentNode.Client._messageIdCounter,
                                                                       _parentNode.Id,
                                                                       inputInBytes);
                    await _parentNode.Client.SendMessageToNode(decreeProposal, _parentNode.PresidentNode, true, true);
                }
            }
        }

        /// <summary>
        /// Whenever a round of Paxos needs to be started (trying to get a decree written), this function needs to be called.
        /// </summary>
        /// <param name="proposedDecree">The decree to write to the ledger</param>
        /// <param name="isFill">If the decree is an unimportant (olive-day) decree</param>
        /// <param name="isNewDecree">If the decree is a new decree (not a learned decree)</param>
        /// <param name="entryId">The id of the decree</param>
        /// <returns></returns>
        public async Task ExecutePaxos(byte[] proposedDecree, bool isFill = false, bool isNewDecree = false, long entryId = 0)
        {
            Cluster quorum = GetOnlineNodes();
            bool initFinished = ((!isNewDecree && !presidentInitTaskFinished) || presidentInitTaskFinished);

            if (_parentNode.isPresident
                && _parentNode.status == NodeStatus.idle
                && initFinished)
            {
                Console.WriteLine("\n[Proposer] Executing Paxos");

                int ballotSuccessful = 1;
                do
                {
                    quorum = GetOnlineNodes();
                    _parentNode.status = NodeStatus.trying;
                    ballotSuccessful = await StartPollingMajoritySet(quorum, proposedDecree, isFill, isNewDecree, entryId);
                } while (ballotSuccessful == 1);

                if (ballotSuccessful == 0)
                {
                    await Succeed();
                }
                else if (ballotSuccessful == 2)
                {
                    Console.WriteLine("[Proposer] Aborting Paxos");
                }
            }
            else if (!initFinished)
            {
                Console.WriteLine("[Proposer] Not ready to conduct ballot.");
            }
        }

        public async Task<int> TryNewBallot()
        {
            if (_parentNode.isPresident)
            {
                _parentNode.status = NodeStatus.trying;
                _parentNode.prevVotes = new List<LastVote>();
                return await SendNextBallotMessage();
            }
            return 2;
        }

        public async Task<int> SendNextBallotMessage()
        {
            TimeAtPreviousAction = DateTime.Now;
            Cluster setOfNodes = GetOnlineNodes();
            bool majorityOnline = setOfNodes.HasMajorityOf(_parentNode.AllNodes);

            if (_parentNode.status == NodeStatus.trying
                && _parentNode.isPresident
                && majorityOnline)
            {
                Console.WriteLine("[Proposer] Sending nextBallotMsg to {0},", String.Join(",", setOfNodes.Keys));
                TimeAtPreviousAction = DateTime.Now;
                long lastEntryUntilMissing = await LedgerHelper.GetLastEntryIdUntilMissingData();
                NextBallot nextBallot = new NextBallot(_parentNode.Client._messageIdCounter, _parentNode.Id, _parentNode.lastTried, lastEntryUntilMissing);

                //send message to peers
                await _parentNode.Client.SendMessageToCluster(nextBallot, setOfNodes.GetClusterExcludingNode(_parentNode), true);


                //send this message to own acceptor
                await _parentNode.Acceptor.OnReceiveNextBallot(nextBallot);

                //wait for reply from set of nodes
                while (_parentNode.prevVotes.Count() != setOfNodes.Count())
                {
                    if ((DateTime.Now - TimeAtPreviousAction).TotalMilliseconds >= Node.MINUTE_IN_PAXOS_TIME * 22)
                    {
                        Console.WriteLine("[Proposer] Didn't get enough lastvote messages in time.");
                        return 1;
                    }
                }
                Console.WriteLine("[Proposer] Received all lastvotes from chosen set of nodes.");
                return 0;
            }
            else if(!majorityOnline)
            {
                return 1;
            }
            else if (!_parentNode.isPresident)
            {
                Console.WriteLine("[Proposer] Cannot send a NextBallot message as a non-president.");
            }
            else if (_parentNode.status != NodeStatus.trying)
            {
                Console.WriteLine("[Proposer] Cannot send a NextBallot message when not trying.");
            }
            return 2;
        }

        public void ReceiveLastVoteMessage(LastVote lastVote)
        {
            if (lastVote._nextBal == _parentNode.lastTried && _parentNode.status == NodeStatus.trying)
            {
                Console.WriteLine("[Proposer] Received lastvote from node {0}.", lastVote._senderId);
                _parentNode.prevVotes.Add(lastVote);
            }
        }

        /// <summary>
        /// If a president learns about decrees from non-presidents, ballots have to be conducted for these decrees.
        /// </summary>
        /// <returns></returns>
        public async Task ConductBallotsMissingDecrees()
        {
            //learn about any missing decrees sent by lastvote from other priests
            if (_parentNode.isPresident && _parentNode.prevVotes.Count() >= 1)
            {
                List<LedgerEntry> entries = await LedgerHelper.GetEntries();
                HashSet<long> decreesInLedger = new HashSet<long>();
                List<string> missingDecreesStrings =
                    _parentNode.prevVotes.Select(v => v._missingDecrees)
                    .Where(s => s.Length > 0).ToList();
                Dictionary<long, byte[]> decreesToPropose = new Dictionary<long, byte[]>();

                foreach (string missingDecreesString in missingDecreesStrings)
                {
                    string[] missingDecrees = missingDecreesString.Split('|');

                    foreach (var missingDecree in missingDecrees)
                    {
                        string[] entryInformation = missingDecree.Split(':');
                        long entryId = long.Parse(entryInformation[0]);
                        byte[] decree = MessageHelper.StringToByteArray(entryInformation[1]);

                        //if not sure in ledger
                        if (!decreesInLedger.Contains(entryId))
                        {
                            LedgerEntry entryInDb = entries.SingleOrDefault(e => e.Id == entryId);
                            if (entryInDb != null && !entryInDb.Decree.Equals(OLIVE_DAY_DECREE))
                            {
                                //just discovered is in ledger
                                //also updates any olive day decrees
                                decreesInLedger.Add(entryId);
                                continue;
                            }
                        }
                        else// if decree known in ledger
                        {
                            continue;
                        }

                        if (!decreesToPropose.ContainsKey(entryId))
                        {
                            decreesToPropose.Add(entryId, decree);
                        }
                    }
                }

                foreach (var decreeToPropose in decreesToPropose)
                {
                    //not in ledger, so needs to be proposed/written
                    Console.WriteLine("\n[Proposer] Learned about [{0}:{1}]. Conducting ballot.",
                                      decreeToPropose.Key,
                                      MessageHelper.ByteArrayToString(decreeToPropose.Value));
                    _parentNode.entryId = decreeToPropose.Key;
                    _parentNode.decree = decreeToPropose.Value;
                    await ExecutePaxos(decreeToPropose.Value, false, false, decreeToPropose.Key);
                }
            }
        }


        /// <summary>
        /// Informs a non-president with its missing decrees.
        /// The selected decrees should not exist in the non-president's ledger
        /// and should be equal to or lower than the number of the decree written in the president't ledger 
        /// up until it is missing decrees.
        /// </summary>
        /// <param name="rmem"></param>
        /// <returns></returns>
        public async Task InformMissingDecrees(RequestMissingEntriesMessage rmem)
        {
            List<long> missingDecreeIds =
                rmem._entriesInOwnLedgerString.Length > 0
                ? rmem._entriesInOwnLedgerString.Split('|').Select(d => long.Parse(d)).ToList()
                : new List<long>();

            string entriesToInformString = await LedgerHelper.GetMissingEntriesForNonPresident(missingDecreeIds);
            InformMissingEntriesMessage informMissingEntriesMessage =
                new InformMissingEntriesMessage(_parentNode.Client._messageIdCounter,
                                                _parentNode.Id,
                                                entriesToInformString);

            if (entriesToInformString.Length > 0)
            {
                Console.WriteLine("[Proposer] Informing {0} with missing decrees: [{1}]", rmem._senderId, entriesToInformString);
                await _parentNode.Client.SendMessageToNode(informMissingEntriesMessage, rmem._senderId, true, true);
            }
        }


        /// <summary>
        /// Begins a ballot for a specified quorum, with a specified decree and decree id
        /// </summary>
        /// <param name="quorum">Collection of nodes, part of the voting process</param>
        /// <param name="proposedDecree">The decree attempted to be written to the distributed ledger</param>
        /// <param name="isFill">If the current decree is for filling with olive-day decrees</param>
        /// <param name="isNewDecree">If the current decree is a decree not written in the ledger yet</param>
        /// <param name="entryId">The decree id</param>
        /// <returns></returns>
        private async Task<int> StartPollingMajoritySet(Cluster quorum, byte[] proposedDecree, bool isFill, bool isNewDecree, long entryId = 0)
        {
            //TODO this doesn't work if nodes leave the network (due to prevVote change).
            //although, the part-time parliament expects no node one to join or leave during operation.
            //This is the main problem mentioned in the thesis. The prevVotes set stays static while the quorum is dynamic.
            //A good look needs to be taken at how the quorum is formed.
            bool quorumMembersAreLastVoters =
                quorum.Keys.ToList()
                .Intersect(_parentNode.prevVotes.Select(lv => lv._senderId).ToList())
                .Count() == quorum.Count();

            if (_parentNode.isPresident
                && _parentNode.status == NodeStatus.trying
                && quorumMembersAreLastVoters)
            {
                Console.WriteLine("[Proposer] Polling...");
                _parentNode.status = NodeStatus.polling;
                _parentNode.quorum = quorum;
                _parentNode.voters = new Cluster();

                _parentNode.isFill = isFill;
                _parentNode.isNewDecree = isNewDecree;

                if (entryId != 0) //if entryId has been given
                {
                    _parentNode.entryId = entryId;
                }
                else //if entryId has not been given
                {
                    if (!isFill) //when it's a normal entry
                    {
                        LedgerEntry lastEntryInDb = null;

                        using (Ledger ledger = new Ledger())
                        {
                            lastEntryInDb = await ledger.Entries.LastOrDefaultAsync();
                        }

                        if (lastEntryInDb != null)
                        {
                            _parentNode.entryId = lastEntryInDb.Id;
                            _parentNode.entryId++;
                        }
                        else
                        {
                            _parentNode.entryId = 1;
                        }

                        if (isNewDecree)
                        {
                            _parentNode.decree = proposedDecree;
                        }
                        else
                        {
                            _parentNode.decree = GetDecreeToPropose(proposedDecree);
                        }
                    }
                    else //when it's filling with olive day decrees
                    {
                        _parentNode.decree = proposedDecree;
                    }
                }

                Console.WriteLine("[Proposer] Ballot: decreeId={0}, decree={1}, quorum={2}", _parentNode.entryId, MessageHelper.ByteArrayToString(_parentNode.decree), String.Join(",", quorum.Keys.ToArray()));
                return await SendBeginBallotMessage();
            }
            return 1;
        }


        private async Task<int> SendBeginBallotMessage()
        {
            if (_parentNode.status == NodeStatus.polling && _parentNode.isPresident)
            {
                TimeAtPreviousAction = DateTime.Now;
                BeginBallot beginBallotMsg = new BeginBallot(_parentNode.Client._messageIdCounter, _parentNode.Id, _parentNode.lastTried, _parentNode.decree);
                await _parentNode.Client.SendMessageToCluster(beginBallotMsg, _parentNode.quorum.GetClusterExcludingNode(_parentNode), true);

                //send this message to own acceptor
                await _parentNode.Acceptor.OnReceiveBeginBallot(beginBallotMsg);

                while (_parentNode.voters.Count() < _parentNode.quorum.Count())
                {
                    //wait for every quorum member to reply
                    if ((DateTime.Now - TimeAtPreviousAction).TotalMilliseconds >= Node.MINUTE_IN_PAXOS_TIME * 22)
                    {
                        return 1;
                    }
                }
                return 0;
            }
            else
            {
                Console.WriteLine("[Proposer] Cannot send beginballot message, because not polling.");
            }
            return 1;
        }

        public void ReceiveVotedMessage(Voted voted)
        {
            if (voted._ballotId == _parentNode.lastTried && _parentNode.status == NodeStatus.polling)
            {
                Console.WriteLine("[Proposer] Received voted message from {0} for {1}.", voted._senderId, voted._ballotId);
                _parentNode.voters.TryAdd(voted._senderId, _parentNode.AllNodes.GetNodeById(voted._senderId));
            }
            else if(voted._ballotId != _parentNode.lastTried)
            {
                Console.WriteLine("[Proposer] Received a voted message from a different kind of ballot. Vote ballotid={0}. lastTried={1}", voted._ballotId, _parentNode.lastTried);
            }
            else if(_parentNode.status != NodeStatus.polling)
            {
                Console.WriteLine("[Proposer] This node is not polling. Not doing anything with voted message.");
                Console.WriteLine("[Proposer] Status = {0}", _parentNode.status);
            }
        }

        /// <summary>
        ///   Checks if the decree has been voted for by the quorum.
        ///   If so, it writes it to the ledger and sends a success message to all other peers
        /// </summary>
        private async Task Succeed()
        {
            TimeAtPreviousAction = DateTime.Now;
            bool quorumMembersAreVoters =
                _parentNode.quorum.Keys.ToList()
                .Intersect(_parentNode.voters.Keys.ToList())
                .Count() == _parentNode.quorum.Count();


            byte[] outcome = await LedgerHelper.GetOutcome(_parentNode.entryId);

            if (_parentNode.status == NodeStatus.polling
                && quorumMembersAreVoters
                && outcome == null || (outcome != null && MessageHelper.ByteArrayToString(outcome).Equals(OLIVE_DAY_DECREE)))
            {
                Console.WriteLine("[Proposer] Ballot [{0}:{1}] succeeded.",
                                  _parentNode.entryId,
                                  MessageHelper.ByteArrayToString(_parentNode.decree));

                Success success = new Success(_parentNode.Client._messageIdCounter,
                                              _parentNode.Id,
                                              _parentNode.decree,
                                              _parentNode.entryId);

                await _parentNode.Learner.WriteSingleDecreeToLedgerImmediately(success);
                await SendSuccessMessage(success);
            }
            else if (!quorumMembersAreVoters)
            {
                Console.WriteLine("[Proposer] Not succeeding ballot. Not all quorum members voted.");
            }
            else if (_parentNode.status != NodeStatus.polling)
            {
                Console.WriteLine("[Proposer] Not succeeding ballot, Node is not polling.");
            }
            else if (outcome != null)
            {
                Console.WriteLine("[Proposer] Not succeeding ballot, Outcome is already known: {0}", MessageHelper.ByteArrayToString(outcome));
            }
            _parentNode.status = NodeStatus.idle;
        }

        /// <summary>
        /// Ballot was successful, and so sends outcome to other nodes
        /// </summary>
        /// <param name="success">Message containing infromation of the decree to write.</param>
        private async Task SendSuccessMessage(Success success)
        {
            byte[] outcome = await LedgerHelper.GetOutcome(_parentNode.entryId);
            if (outcome != null)
            {
                Console.WriteLine("[Proposer] Sending passed decree [{0}:{1}] to learners.", _parentNode.entryId, MessageHelper.ByteArrayToString(outcome));
                await _parentNode.Client.SendMessageToCluster(success, _parentNode.Peers, true);
            }
            else
            {
                Console.WriteLine("[Proposer] Not sending success message, since outcome is not known.");
            }
        }

        private Cluster GetOnlineNodes()
        {
            Cluster quorum = new Cluster(_parentNode.OnlinePeers);
            quorum.TryAdd(_parentNode.Id, _parentNode);
            return quorum;
        }

        /// <summary>
        /// Gets the decree to propose based on requirement 3 (B3 from Part-Time Parliament)
        /// </summary>
        /// <param name="proposedDecree">The decree proposed for this ballot</param>
        private byte[] GetDecreeToPropose(byte[] proposedDecree)
        {
            foreach (LastVote lv in _parentNode.prevVotes)
            {
                Console.WriteLine(lv.ToString());
            }

            LastVote highestLastVote = _parentNode.prevVotes.OrderByDescending(v => v._prevBal).First();

            if (highestLastVote._prevBal != decimal.MinValue)
            {
                return highestLastVote._prevDecree;
            }
            else
            {
                return proposedDecree;
            }
        }

        /// <summary>
        /// The steps required to take once a node becomes president
        /// </summary>
        public async Task OnBecomingPresident()
        {
            if (!presidentInitTaskFinished && _parentNode.isPresident)
            {
                Console.WriteLine("[Proposer] Learning decrees...");
                int newBallotMsgResult = 1;
                await IncrementBallotId();

                do
                {
                    //execute step 1-2 to learn about decrees and prepare for steps 3-6
                    newBallotMsgResult = await TryNewBallot();
                } while (newBallotMsgResult != 0 && _parentNode.isPresident);

                if (newBallotMsgResult == 0)
                {
                    _parentNode.status = NodeStatus.idle;
                    await ConductBallotsMissingDecrees();
                    await FillGapsInLedger();
                    presidentInitTaskFinished = true;
                    InitExecutePaxosWhenProposerTask();
                }
            }
        }

        /// <summary>
        /// Execute Paxos whenever a decree is required to be written to the ledger (see collection: proposals)
        /// </summary>
        private void InitExecutePaxosWhenProposerTask()
        {
            ExecutePaxosOnReceivedProposalThread = new Thread(async () =>
                    {
                        while (presidentInitTaskFinished)
                        {
                            if (Proposals.Count() > 0
                                && _parentNode.status == NodeStatus.idle
                                && GetOnlineNodes().HasMajorityOf(_parentNode.AllNodes))
                            {
                                byte[] toBallot = Proposals.Dequeue();
                                await ExecutePaxos(toBallot, false, true, 0);
                            }
                        }
                    });
            ExecutePaxosOnReceivedProposalThread.Start();
        }

        /// <summary>
        /// Fills missing decrees with unimportant decrees by attempting to pass ballots
        /// </summary>
        public async Task FillGapsInLedger()
        {
            List<int> missingEntryIds;

            if (_parentNode.isPresident)
            {
                List<LedgerEntry> entries = await LedgerHelper.GetEntries();
                List<int> writtenEntryIds = entries.Select(e => (int)e.Id).ToList();

                if (writtenEntryIds.Any())
                {
                    missingEntryIds = Enumerable.Range(1, (int)writtenEntryIds.Last()).Except(writtenEntryIds).ToList();

                    foreach (int entryId in missingEntryIds)
                    {
                        if (_parentNode.isPresident)
                        {
                            Console.WriteLine("\n[Proposer] Attempting to fill decree {0} with olive day decree.", entryId);
                            _parentNode.entryId = entryId;
                            _parentNode.decree = MessageHelper.StringToByteArray(OLIVE_DAY_DECREE);
                            await ExecutePaxos(_parentNode.decree, true, false, entryId);
                        }
                        else
                        {
                            return;
                        }
                    }

                    if (missingEntryIds.Count() > 0)
                    {
                        Console.WriteLine("[Proposer] Filled gaps in ledger with olive day decree.");
                    }
                }
                else
                {
                    Console.WriteLine("[Proposer] No gaps to fill with olive day decrees.");
                }
            }
        }

        /// <summary>
        /// Whenever the president is notified with a nextBal, update lasttried and execute Paxos
        /// THIS METHOD IS NOT FINISHED. How does one abort a current execution of paxos?
        /// </summary>
        /// <param name="nextBal">The updated ballot number</param>
        /// <returns></returns>
        public async Task ReceiveNewerBallotNumber(decimal nextBal)
        {
            //update ballot number
            _parentNode.lastTried = MessageHelper.CreateUniqueMessageId((long)Math.Truncate(nextBal), _parentNode.Id);
            // TODO how to stop the current Paxos execution, and restart with newer ballot number?
            //await ExecutePaxos(_parentNode.decree,
            //                   _parentNode.isFill,
            //                   _parentNode.isNewDecree,
            //                   _parentNode.entryId);
        }

        /// <summary>
        /// Add a decree to a collection of decrees which need to be added to the distributed ledger.
        /// Whenever the Proposer && president are inactive and has a decree in this collection, Paxos
        /// will be executed for this decree.
        /// </summary>
        /// <param name="decreeProposal"></param>
        public void OnDecreeProposal(DecreeProposal decreeProposal)
        {
            Proposals.Enqueue(decreeProposal._decree);
        }

        private async Task IncrementBallotId()
        {
            if (_parentNode.lastTried == decimal.MinValue)
            {
                _parentNode.lastTried = MessageHelper.CreateUniqueMessageId(1, _parentNode.Id);
            }
            else
            {
                _parentNode.lastTried++;
            }

            await LedgerHelper.SavePaxosProgressAsync(_parentNode);
            Console.WriteLine("[Proposer] Ballot id incremented to: {0}", _parentNode.lastTried);
        }
    }
}
