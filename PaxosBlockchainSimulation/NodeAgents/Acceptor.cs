using System;
using PaxosBlockchainSimulation.Messaging;
using System.Threading.Tasks;
using PaxosBlockchainSimulation.Database;

namespace PaxosBlockchainSimulation.NodeAgents
{
    /// <summary>
    /// The acceptor is responsible for saying if it will take part in a ballot.
    /// It can also vote for ballots.
    /// The acceptor will send its messages back to the president.
    /// </summary>
    public class Acceptor
    {
        private Node _parentNode;

        public Acceptor(Node node)
        {
            _parentNode = node;
        }

        public async Task OnReceiveNextBallot(NextBallot nextBallotMsg)
        {
            if (nextBallotMsg._ballotId >= _parentNode.nextBal)
            {
                Console.WriteLine("[Acceptor] Received new nextballot message.");
                _parentNode.nextBal = nextBallotMsg._ballotId;
                await SendLastVoteMessage(nextBallotMsg);
                await LedgerHelper.SavePaxosProgressAsync(_parentNode);
            }
            else if (nextBallotMsg._ballotId < _parentNode.nextBal)
            {
                Console.WriteLine("[Acceptor] Received old nextballot message.");
                await ReplyNextBal(nextBallotMsg._senderId);
            }
        }

        /// <summary>
        ///   Informs president p of previously voted for decrees.
        ///   It's a promise of node q to not vote for a ballot with a lower id.
        ///   Also informs the president p of any missing decrees, which node q has.
        /// </summary>
        private async Task SendLastVoteMessage(NextBallot nextBallotMsg)
        {
            if (_parentNode.nextBal > _parentNode.prevBal)
            {
                string missingDecreesPresidentString
                    = nextBallotMsg._senderId == _parentNode.Id
                    ? ""
                    : await LedgerHelper.GetMissingEntriesPresident(_parentNode.Id,
                                                                    nextBallotMsg._senderId,
                                                                    nextBallotMsg._hasDecreesUntil);

                LastVote lastVote = new LastVote(_parentNode.Client._messageIdCounter,
                                                 _parentNode.nextBal,
                                                 _parentNode.Id,
                                                 _parentNode.prevBal,
                                                 _parentNode.prevDec,
                                                 missingDecreesPresidentString);

                if (nextBallotMsg._senderId == _parentNode.Id)
                {
                    Console.WriteLine("[Acceptor] Sending LastVote to self");
                    _parentNode.Proposer.ReceiveLastVoteMessage(lastVote);
                }
                else
                {
                    Console.WriteLine("[Acceptor] Sending LastVote to president (node {0})", nextBallotMsg._senderId);
                    Node votingProposer = _parentNode.OnlinePeers.GetNodeById(nextBallotMsg._senderId);
                    await _parentNode.Client.SendMessageToNode(lastVote, votingProposer, true, true);
                    await RequestMissingEntries(nextBallotMsg);
                }
            }
            else
            {
                Console.WriteLine("[Acceptor] !!!Not sending lastvote because prevBal({0}) is higher than nextBal({1})!!!", _parentNode.nextBal, _parentNode.prevBal);
            }
        }

        /// <summary>
        /// Ask the president of any missing decrees lower than and equal to n.
        /// Where n is the number up until the president p found a gap in the ledger (missed decree)
        /// </summary>
        /// <param name="nextBallotMsg"></param>
        private async Task RequestMissingEntries(NextBallot nextBallotMsg)
        {
            string missingDecreesString = await LedgerHelper.GetMissingDecreesString(nextBallotMsg._hasDecreesUntil);
            RequestMissingEntriesMessage request = new RequestMissingEntriesMessage(_parentNode.Client._messageIdCounter,
                                                                                    _parentNode.Id,
                                                                                    nextBallotMsg._hasDecreesUntil,
                                                                                    missingDecreesString);

            Console.WriteLine("[Acceptor] Requesting any potential missing decrees (id <= {0}) from president.", nextBallotMsg._hasDecreesUntil);
            await _parentNode.Client.SendMessageToNode(request, nextBallotMsg._senderId, true, true);
        }

        public async Task OnReceiveBeginBallot(BeginBallot beginBallotMsg)
        {
            Console.WriteLine("[Acceptor] Received beginballot [{0}] from {1}",
                              beginBallotMsg._ballotId,
                              beginBallotMsg._senderId);

            if (beginBallotMsg._ballotId == _parentNode.nextBal)
            {
                Console.WriteLine("[Acceptor] Voting for ballot...");
                _parentNode.prevBal = beginBallotMsg._ballotId;
                _parentNode.prevDec = beginBallotMsg._decree;
                await SendVotedMessage(beginBallotMsg);
                await LedgerHelper.SavePaxosProgressAsync(_parentNode);
            }
            else if (beginBallotMsg._ballotId < _parentNode.nextBal)
            {
                Console.WriteLine("b={0} < nextBal={1}",
                                  beginBallotMsg._ballotId,
                                  _parentNode.nextBal);
                await ReplyNextBal(beginBallotMsg._senderId);
            }
        }

        private async Task SendVotedMessage(BeginBallot beginBallotMsg)
        {
            if (_parentNode.prevBal != decimal.MinValue)
            {
                Voted voted = new Voted(_parentNode.Client._messageIdCounter,
                                        _parentNode.Id,
                                        _parentNode.prevBal);

                if (beginBallotMsg._senderId == _parentNode.Id)
                {
                    //send to self
                    Console.WriteLine("[Acceptor] Sending voted to self");
                    _parentNode.Proposer.ReceiveVotedMessage(voted);
                }
                else
                {
                    //send to president
                    Console.WriteLine("[Acceptor] Sending voted to {0}", beginBallotMsg._senderId);
                    Node president = _parentNode.Peers.GetNodeById(beginBallotMsg._senderId);
                    await _parentNode.Client.SendMessageToNode(voted, president, true, true);
                }
            }
            else
            {
                Console.WriteLine("[Acceptor] Cannot cast vote because prevBal has not been set.");
            }
        }

        /// <summary>
        ///   Inform the president with the latest ballot number.
        /// </summary>
        private async Task ReplyNextBal(int peerId)
        {
                Console.WriteLine("[Acceptor] Sending nextBal to president {0}.", peerId);
                UpdateBallotNumber newBallot = new UpdateBallotNumber(_parentNode.Client._messageIdCounter, _parentNode.Id, _parentNode.nextBal);
                await _parentNode.Client.SendMessageToNode(newBallot, peerId, true, true);
        }
    }
}
