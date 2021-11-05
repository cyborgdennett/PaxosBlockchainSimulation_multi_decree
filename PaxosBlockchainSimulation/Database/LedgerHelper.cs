using System;
using System.Collections.Generic;
using System.Linq;
using PaxosBlockchainSimulation.NodeAgents;
using PaxosBlockchainSimulation.Messaging;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace PaxosBlockchainSimulation.Database
{
    /// <summary>
    /// This class contains most operations which require database access.
    /// To access the local database, the Ledger class will be accessed, 
    /// which is an extention of the DBContext class (from Entity Framework).
    /// </summary>
    public static class LedgerHelper
    {

        /// <summary>
        ///   If a newly elected president p has all
        ///   decrees with numbers less than or equal to n written in his ledger, it will return n.
        /// </summary>
        public static async Task<long> GetLastEntryIdUntilMissingData()
        {
            long previousId = 0;
            List<LedgerEntry> entries = await GetEntries();

            if (entries.Count() >= 1)
            {
                for (int i = 0; i < entries.LastOrDefault().Id; i++)
                {
                    LedgerEntry entry = entries.ElementAt(i);

                    if (entry.Id != previousId + 1 || entry.Decree.Equals(Proposer.OLIVE_DAY_DECREE))
                        //TODO stop at olive decrees? See Paxos Blockchain Addendum
                    {
                        break;
                    }
                    else
                    {
                        previousId = entry.Id;
                    }
                }
            }
            return previousId;
        }

        /// <summary>
        ///   Creates a string which contains the decrees president p is missing, and which node q does have.
        ///   Say, president p has up to decree 2, he will ask for all decrees q has, higher than 2.
        ///   Node q might then send: "3:Hello|4:How are you?" (which is what this method returns)
        /// </summary>
        public static async Task<string> GetMissingEntriesPresident(int parentNodeId, int requestingNodeId, long decreeId)
        {
            string missingEntriesString = "";
            List<LedgerEntry> entriesToInformPresident = new List<LedgerEntry>();

            using (Ledger ledger = new Ledger())
            {
                entriesToInformPresident = await
                    ledger.Entries
                    .Where(e => e.Id > decreeId)
                    .Where(e => !e.Decree.Equals(Proposer.OLIVE_DAY_DECREE))
                    .ToListAsync();
            }

            if (entriesToInformPresident.Count > 0)
            {
                for (int i = 0; i < entriesToInformPresident.Count; i++)
                {
                    missingEntriesString += String.Format("{0}:{1}",
                                                          entriesToInformPresident.ElementAt(i).Id,
                                                          entriesToInformPresident.ElementAt(i).Decree);
                    if (i < entriesToInformPresident.Count - 1)
                    {
                        missingEntriesString += "|";
                    }
                }
            }

            return missingEntriesString;
        }

        /// <summary>
        /// Creates a string of all of the decrees (decree ids) the node q is missing.
        /// </summary>
        /// <param name="hasDecreesUntil">Number until president p is missing decrees</param>
        /// <returns>A string with all missing decree ids</returns>
        public async static Task<string> GetMissingDecreesString(long hasDecreesUntil)
        {
            List<LedgerEntry> writtenEntries = await GetEntries();
            List<int> writtenDecreeIds = writtenEntries.Select(e => (int)e.Id).ToList();
            List<int> allDecreeIds = Enumerable.Range(1, (int) hasDecreesUntil).ToList();
            List<int> missingEntries = allDecreeIds.Except(writtenDecreeIds).ToList();
            return string.Join("|", missingEntries);
        }


        /// <summary>
        /// President p will call this method to get the decrees non-president q is missing. 
        /// </summary>
        /// <param name="missingDecreeIds">The decree ids which non-president q is missing</param>
        /// <returns>A string of decrees (which non-president is missing), to be sent to non-president q</returns>
        public static async Task<string> GetMissingEntriesForNonPresident(List<long> missingDecreeIds)
        {
            string missingEntriesString = "";
            List<LedgerEntry> entriesToInform = new List<LedgerEntry>();

            using (Ledger ledger = new Ledger())
            {
                entriesToInform = await ledger.Entries
                    .Where(en => missingDecreeIds.Contains(en.Id))
                    .ToListAsync();
            }

            if (entriesToInform.Count > 0)
            {
                for (int i = 0; i < entriesToInform.Count; i++)
                {
                    missingEntriesString += String.Format("{0}:{1}",
                                                          entriesToInform.ElementAt(i).Id,
                                                          entriesToInform.ElementAt(i).Decree);
                    if (i < entriesToInform.Count - 1)
                    {
                        missingEntriesString += "|";
                    }
                }
            }
            return missingEntriesString;
        }

        /// <summary>
        /// Gets all written entries
        /// </summary>
        /// <returns>A list of all written entries</returns>
        public static async Task<List<LedgerEntry>> GetEntries()
        {
            using (Ledger ledger = new Ledger())
            {
                return await ledger.Entries.ToListAsync();
            }
        }

        /// <summary>
        /// Returns a value for a specified decree id, if a value has been written for that decree id.
        /// </summary>
        /// <param name="decreeId">Position in the ledger to check for a value</param>
        /// <returns>Byte array if a value exists for decreeId. Null if there is no value at decreeId</returns>
        public static async Task<byte[]> GetOutcome(long decreeId)
        {
            LedgerEntry outcomeEntry = null;
            using (Ledger ledger = new Ledger())
            {
                outcomeEntry = await ledger.Entries.FindAsync(decreeId);
            }

            if (outcomeEntry == null)
            {
                return null;
            }
            else
            {
                return MessageHelper.StringToByteArray(outcomeEntry.Decree);
            }
        }

        /// <summary>
        /// Saves the Paxos progress valuables
        /// </summary>
        /// <param name="node">The node's values to save (always the node of the local instance)</param>
        public static async Task SavePaxosProgressAsync(Node node)
        {
            Task task = Task.Factory.StartNew(async () =>
                    {
                        using (Ledger ledger = new Ledger())
                        {
                            PaxosProgress progress = await ledger.Progress.SingleAsync();
                            progress.LastTried = node.lastTried;
                            progress.NextBal = node.nextBal;
                            progress.PrevBal = node.prevBal;
                            progress.PrevDec = node.prevDec;
                            await ledger.SaveChangesAsync();
                        }
                    });
            await task;
        }
    }
}
