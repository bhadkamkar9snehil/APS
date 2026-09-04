using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations;

/// <summary>
/// Repairs the persisted demo/master rows behind the September 4 planning blocker.
/// STD-BAR was configured as a HotRoll step from BLT-150SQ back to BLT-150SQ while
/// the live manufacturing requirement produces RND-12. The route and its matching
/// capability rows must describe the actual billet-to-bar transformation.
///
/// This intentionally touches only the known contradictory STD-BAR/HotRoll rows and
/// leaves historical Plan Version snapshots unchanged.
/// </summary>
[DbContext(typeof(ApsDbContext))]
[Migration("20260904190000_RepairStdBarMasterData")]
public sealed class RepairStdBarMasterData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        const int hotRoll = 6;

        migrationBuilder.Sql($"""
            UPDATE ManufacturingRouteOperations
               SET OutputCrossSectionCode = 'RND-12'
             WHERE RouteCode = 'STD-BAR'
               AND ProcessOperationType = {hotRoll}
               AND InputCrossSectionCode = 'BLT-150SQ'
               AND OutputCrossSectionCode = 'BLT-150SQ';
            """);

        migrationBuilder.Sql($"""
            UPDATE RouteResourceCapabilities
               SET OutputCrossSectionCode = 'RND-12'
             WHERE RouteCode = 'STD-BAR'
               AND ProcessOperationType = {hotRoll}
               AND InputCrossSectionCode = 'BLT-150SQ'
               AND OutputCrossSectionCode = 'BLT-150SQ';
            """);

        migrationBuilder.Sql($"""
            UPDATE ResourceCapabilities
               SET OutputCrossSectionCode = 'RND-12'
             WHERE RouteCode = 'STD-BAR'
               AND (ProcessOperationType = {hotRoll} OR ProcessOperationType IS NULL)
               AND InputCrossSectionCode = 'BLT-150SQ'
               AND OutputCrossSectionCode = 'BLT-150SQ';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately irreversible: restoring the known-invalid route/capability data would
        // reintroduce a configuration that cannot manufacture its assigned production order.
    }
}
