# Paxos blockchain simulation

## About
The Paxos blockchain simulation is a proof of concept made as part of a bachelor thesis. It was made to show the functionality of the Paxos algorithm based on the article The Part-Time Parliament, by Leslie Lamport.

## Prerequisites
- Dotnet core 2.1.1 or newer.

## Installation

1. Follow the official dotnet core installation guide [here](https://dotnet.github.io/).
2. Clone this repository.
3. Using the command line, move to PaxosBlockchainSimulation/ of the cloned repository, and execute: `dotnet add package Microsoft.EntityFrameworkCore.Sqlite` to install the Microsoft SQL database provider. You can use any other database provider. You can find them [here](https://docs.microsoft.com/en-us/ef/core/providers/index).
4. Subsequently, run `dotnet add package Microsoft.EntityFrameworkCore.Design`.
5. Subsequently, run 'dotnet add package System.IO.Ports'.
6. Next, add the following to `PaxosBlockchainSimulation.csproj` (with version being the dotnet version installed): 
```
<ItemGroup>
  <DotNetCliToolReference Include="Microsoft.EntityFrameworkCore.Tools.DotNet" Version="2.0.2" />
</ItemGroup>
```
6. Run `dotnet restore`

## Running the application
-  Execute `dotnet run` from the command line to run the application.

## Known issues and improvements 
There are some known issues and optional improvements. Some are covered in the addendum. Others are explained here

### Known issues
1. On automating three nodes, the application came to a halt about two times around 4500 decrees. It seemed that the applications didn't crash, but instead slowed down an incredible amount (perhaps exponentially?). More details can be viewed on adding timestamps for each decree on reaching agreement.
2. Retrying ballot with a higher ballot id. (See Part Time Parliament, Page 13, end of parahraph 2. Code is in: Proposer.ReceiveNewerBallotNumber())
3. No proper way of maintaining immutability, aside from SQL triggers (which does not prevent anyone from inserting new data).
4. Participation and authorisation of the network. The network information is kept in a text file on each node (this includes endpoints for every node).
5. At the moment, only LAN is supported.
6. With entity framework/sqlite, receiving a high number of success messages could not be processed, so some are skipped or lost. Using a different database might solve this issue, or the solution lies somewhere else.
7. With the multi-decree protocol, the prevVotes[p] group is static, but the quorum is dynamic. Research must be conducted on how consensus can be reached safely after a president has been assigned AND it finished all of its initial tasks.

### Improvements
1. Applying a state machine, instead of reaching consensus over just a log of strings.
2. Combining multiple messages into one (See end of 3.2.2 in Part-Time Parliament)
3. Implementing the Paxos additions covered in the Part-Time Parliament
4. Implementing/researching the further Paxos articles written by Leslie Lamport
5. Do research on Microsoft's CoCo framework's implementation on Paxos.
6. Find a way to implement Byzantine Fault Tolerance
