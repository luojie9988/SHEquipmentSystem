using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SHEquipmentSystem.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlidMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AlidId = table.Column<uint>(type: "INTEGER", nullable: false),
                    AlarmName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PlcAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsMonitored = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoClearEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TriggerAddress = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlidMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CeidMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CeidId = table.Column<uint>(type: "INTEGER", nullable: false),
                    EventName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TriggerAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    TriggerType = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CeidMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EcidMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EcidId = table.Column<uint>(type: "INTEGER", nullable: false),
                    ParameterName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PlcAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DataType = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultValue = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MinValue = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    MaxValue = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Units = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IsReadOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EcidMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RptidMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RptidId = table.Column<uint>(type: "INTEGER", nullable: false),
                    ReportName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RptidMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SvidMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SvidId = table.Column<uint>(type: "INTEGER", nullable: false),
                    SvidName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PlcAddress = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DataType = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Units = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SvidMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RptidSvidMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RptidMappingId = table.Column<int>(type: "INTEGER", nullable: false),
                    SvidId = table.Column<uint>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    SvidMappingId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RptidSvidMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RptidSvidMappings_RptidMappings_RptidMappingId",
                        column: x => x.RptidMappingId,
                        principalTable: "RptidMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RptidSvidMappings_SvidMappings_SvidMappingId",
                        column: x => x.SvidMappingId,
                        principalTable: "SvidMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RptidSvidMappings_RptidMappingId",
                table: "RptidSvidMappings",
                column: "RptidMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_RptidSvidMappings_SvidMappingId",
                table: "RptidSvidMappings",
                column: "SvidMappingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlidMappings");

            migrationBuilder.DropTable(
                name: "CeidMappings");

            migrationBuilder.DropTable(
                name: "EcidMappings");

            migrationBuilder.DropTable(
                name: "RptidSvidMappings");

            migrationBuilder.DropTable(
                name: "RptidMappings");

            migrationBuilder.DropTable(
                name: "SvidMappings");
        }
    }
}
