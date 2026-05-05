using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellyfin.Server.Implementations.Migrations
{
    /// <inheritdoc />
    public partial class Federation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FederationActors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    InboxUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    OutboxUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationActors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FederationOutboxActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityJson = table.Column<string>(type: "TEXT", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationOutboxActivities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FederationActorQueues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationActorQueues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederationActorQueues_FederationActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "FederationActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FederationFollowers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationFollowers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederationFollowers_FederationActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "FederationActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FederationFollowings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationFollowings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederationFollowings_FederationActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "FederationActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FederationFollowRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Responded = table.Column<bool>(type: "INTEGER", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationFollowRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederationFollowRequests_FederationActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "FederationActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FederationIngestedItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BaseItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DateIngested = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationIngestedItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederationIngestedItems_FederationActors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "FederationActors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FederationActorQueueItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    QueueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationActorQueueItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederationActorQueueItems_FederationActorQueues_QueueId",
                        column: x => x.QueueId,
                        principalTable: "FederationActorQueues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FederationStreamTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenHash = table.Column<byte[]>(type: "BLOB", nullable: false),
                    FollowerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateCreated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FederationStreamTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FederationStreamTokens_FederationFollowers_FollowerId",
                        column: x => x.FollowerId,
                        principalTable: "FederationFollowers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FederationActorQueueItems_QueueId_DateCreated",
                table: "FederationActorQueueItems",
                columns: new[] { "QueueId", "DateCreated" });

            migrationBuilder.CreateIndex(
                name: "IX_FederationActorQueues_ActorId",
                table: "FederationActorQueues",
                column: "ActorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FederationActorQueues_NextAttemptAt",
                table: "FederationActorQueues",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_FederationActors_Url",
                table: "FederationActors",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FederationFollowers_ActorId",
                table: "FederationFollowers",
                column: "ActorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FederationFollowings_ActorId",
                table: "FederationFollowings",
                column: "ActorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FederationFollowRequests_ActorId_Type",
                table: "FederationFollowRequests",
                columns: new[] { "ActorId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FederationFollowRequests_Type",
                table: "FederationFollowRequests",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_FederationIngestedItems_ActorId_SourceId",
                table: "FederationIngestedItems",
                columns: new[] { "ActorId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FederationIngestedItems_BaseItemId",
                table: "FederationIngestedItems",
                column: "BaseItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FederationOutboxActivities_DateCreated",
                table: "FederationOutboxActivities",
                column: "DateCreated");

            migrationBuilder.CreateIndex(
                name: "IX_FederationStreamTokens_ExpiresAt",
                table: "FederationStreamTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_FederationStreamTokens_FollowerId",
                table: "FederationStreamTokens",
                column: "FollowerId");

            migrationBuilder.CreateIndex(
                name: "IX_FederationStreamTokens_TokenHash",
                table: "FederationStreamTokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FederationActorQueueItems");

            migrationBuilder.DropTable(
                name: "FederationFollowings");

            migrationBuilder.DropTable(
                name: "FederationFollowRequests");

            migrationBuilder.DropTable(
                name: "FederationIngestedItems");

            migrationBuilder.DropTable(
                name: "FederationOutboxActivities");

            migrationBuilder.DropTable(
                name: "FederationStreamTokens");

            migrationBuilder.DropTable(
                name: "FederationActorQueues");

            migrationBuilder.DropTable(
                name: "FederationFollowers");

            migrationBuilder.DropTable(
                name: "FederationActors");
        }
    }
}
