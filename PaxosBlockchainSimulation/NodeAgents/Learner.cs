using System;
using System.Collections.Generic;
using PaxosBlockchainSimulation.Messaging;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using PaxosBlockchainSimulation.Database;

namespace PaxosBlockchainSimulation.NodeAgents
{
    /// <summary>
    /// The learner is responsible for learning and writing decrees to the shared ledger.

    /// A special mechanism has been added to write multiple decrees all at once (from Success messages).
    /// This is done after every TIME_BETWEEN_WRITE_CHECK_SECONDS;
    /// Enabled by default (see writeImmediately). 
    /// In the part-time parliament, writing should be done immediately on receiving a success message.
    /// The Entity Framework Sqlite database provider is probably limited, because sometimes it would not do anything with success messages.
    /// When writing multiple decrees at once, there was less chance of failure. That's why this mechanism was implemented.
    /// Perhaps using a database such as postgres/mysql/oracle could prove to be more stable, so that this mechanism would not be needed.
    /// </summary>
    public class Learner
    {
        private Node _parentNode;
        private readonly bool writeImmediately = true;
        private DateTime lastSuccessReceived;
        private List<LedgerEntry> receivedEntries;
        private static readonly double TIME_BETWEEN_WRITE_CHECK_SECONDS = 0.75;

        public Learner(Node parentNode)
        {
            _parentNode = parentNode;

            if (!writeImmediately)
            {
                InitSaveLedgerChangesTask();
            }
        }


        /// <summary>
        /// The special mechanism mentioned in the class description.
        /// Only writes decrees to ledger if writeImmediately is disabled.
        /// Otherwise, decrees are written in an instant.
        /// </summary>
        private void InitSaveLedgerChangesTask()
        {
            if (!writeImmediately)
            {
                lastSuccessReceived = DateTime.MinValue;
                receivedEntries = new List<LedgerEntry>();
                Task.Factory.StartNew(async () =>
                        {
                            while (true)
                            {
                                if (receivedEntries.Count >= 1
                                    && (DateTime.Now - lastSuccessReceived).TotalSeconds >= TIME_BETWEEN_WRITE_CHECK_SECONDS)
                                {
                                    //copy items over to make sure no values are lost
                                    List<LedgerEntry> entriesToWrite = new List<LedgerEntry>(receivedEntries);
                                    await AddOrUpdateLedgerEntries(entriesToWrite);
                                    receivedEntries = receivedEntries.Except(entriesToWrite).ToList();
                                }
                            }
                        });
            }
        }

        /// <summary>
        /// Whenever a ballot was successful, the decree is sent from the president node
        /// to the non-presidents with a Success message. This method is executed whenever 
        /// such a success message arrives.
        /// </summary>
        /// <param name="success">The received Success message.</param>
        public async Task ReceiveSuccess(Success success)
        {
            if (!writeImmediately)
            {
                LedgerEntry entry = new LedgerEntry
                {
                    Id = success._decreeId,
                    Decree = MessageHelper.ByteArrayToString(success._decree)
                };

                lastSuccessReceived = DateTime.Now;
                receivedEntries.Add(entry);
            }
            else
            {
                await WriteSingleDecreeToLedgerImmediately(success);
            }
        }

        public async Task WriteSingleDecreeToLedgerImmediately(Success success)
        {
            LedgerEntry entry = new LedgerEntry
            {
                Id = success._decreeId,
                Decree = MessageHelper.ByteArrayToString(success._decree)
            };
            await AddOrUpdateSingleEntry(entry, null);
        }

        /// <summary>
        /// A list of ledger entries can be written simultaneously by calling this method.
        /// Every item is added (or updated if it's an unimportant decree -- the olive decree),
        /// and finally, the changes are written to the database.
        /// </summary>
        private async Task AddOrUpdateLedgerEntries(List<LedgerEntry> entriesToWrite)
        {
            using (Ledger ledger = new Ledger())
            {
                foreach (LedgerEntry entryToWrite in entriesToWrite)
                {
                    await AddOrUpdateSingleEntry(entryToWrite, ledger);
                }
                await ledger.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Inserts a new entry, or overwrites an unimportant entry (olive-day decree).
        /// The ledger argument is required, because another method uses this method to insert/update 
        /// and save a number of decrees at once. That way, no ledger has to be created for every decree.
        /// </summary>
        /// <param name="entryToWrite">The entry to write</param>
        /// <param name="ledger">The ledger instance to write the data to.</param>
        private async Task AddOrUpdateSingleEntry(LedgerEntry entryToWrite, Ledger ledger)
        {
            bool doSave = false;
            if (ledger == null)
            {
                doSave = true;
                ledger = new Ledger();
            }

            LedgerEntry entryInDb = await ledger.Entries.SingleOrDefaultAsync(e => e.Id == entryToWrite.Id);

            if (entryInDb == null)
            {
                ledger.Entries.Add(entryToWrite);
                Console.WriteLine("[Learner] Written new decree [{0}:{1}]",
                                  entryToWrite.Id,
                                  entryToWrite.Decree);
            }
            else if (entryInDb.Decree.Equals(Proposer.OLIVE_DAY_DECREE))
            {
                entryInDb.Decree = entryToWrite.Decree;
                Console.WriteLine("[Learner] Updated decree [{0}:{1}]",
                                  entryInDb.Id,
                                  entryInDb.Decree);
            }

            if (doSave)
            {
                await ledger.SaveChangesAsync();
            }
        }

        /// <summary>
        /// This method is executed when a non-president receives missing decrees from a president.
        /// A missing decree string looks like this: "1:Test1|2:Test2|3:Test3"
        /// </summary>
        /// <param name="missingDecreesString">The string with missing decrees</param>
        public async Task WriteMissingDecreesToLedger(string missingDecreesString)
        {
            //learn about any missing decrees sent by lastvote from other priests
            if (missingDecreesString.Length > 0)
            {
                Console.WriteLine("[Learner] Writing missing entries to ledger.");
                string[] missingDecrees = missingDecreesString.Split('|');
                List<LedgerEntry> ledgerEntriesToWrite = new List<LedgerEntry>();

                foreach (var missingDecree in missingDecrees)
                {
                    string[] entryInformation = missingDecree.Split(':');
                    LedgerEntry ledgerEntry = new LedgerEntry
                    {
                        Id = long.Parse(entryInformation[0]),
                        Decree = entryInformation[1]
                    };
                    ledgerEntriesToWrite.Add(ledgerEntry);
                }
                await AddOrUpdateLedgerEntries(ledgerEntriesToWrite);
            }
        }
    }
}
