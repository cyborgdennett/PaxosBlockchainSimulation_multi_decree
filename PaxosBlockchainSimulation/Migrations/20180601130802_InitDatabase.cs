using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

namespace PaxosBlockchainSimulation.Migrations
{
    public partial class InitDatabase : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Entries",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Decree = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Progress",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastTried = table.Column<decimal>(nullable: false),
                    NextBal = table.Column<decimal>(nullable: false),
                    PrevBal = table.Column<decimal>(nullable: false),
                    PrevDec = table.Column<byte[]>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Progress", x => x.Id);
                });

            //immutability trigger for SQLite. Different DB providers require different SQL statements
            migrationBuilder.Sql(String.Format(
@"
CREATE TRIGGER AttemptUpdate BEFORE UPDATE ON Entries FOR EACH ROW
WHEN old.Decree != '{0}'
BEGIN
 SELECT RAISE(ABORT, 'Ledgers are written with indelible ink, and their entries can not be changed');
END;

CREATE TRIGGER AttemptDeletion BEFORE DELETE ON Entries FOR EACH ROW
BEGIN
 SELECT RAISE(ABORT, 'Ledgers are written with indelible ink, and their entries can not be changed');
END;
", NodeAgents.Proposer.OLIVE_DAY_DECREE));


            migrationBuilder.InsertData(
                table: "Progress",
                columns: new[] { "Id", "LastTried", "NextBal", "PrevBal", "PrevDec" },
                values: new object[] { 1, -79228162514264337593543950335m, -79228162514264337593543950335m, -79228162514264337593543950335m, new byte[] {  } });

        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entries");

            migrationBuilder.DropTable(
                name: "Progress");
        }
    }
}
