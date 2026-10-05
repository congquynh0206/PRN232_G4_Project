using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace G4.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM Coupon WHERE code IS NULL OR LEN(UPPER(LTRIM(RTRIM(code)))) NOT BETWEEN 3 AND 50
                    OR UPPER(LTRIM(RTRIM(code))) COLLATE Latin1_General_100_BIN2 LIKE '%[^A-Z0-9_-]%'
                    OR discountPercent IS NULL OR discountPercent<=0 OR discountPercent>100
                    OR (startDate IS NOT NULL AND endDate IS NOT NULL AND endDate<=startDate) OR maxUsage<=0)
                    THROW 51001,'Legacy coupons contain invalid codes, dates or limits. Review before migrating.',1;
                IF EXISTS (SELECT UPPER(LTRIM(RTRIM(code))) FROM Coupon GROUP BY UPPER(LTRIM(RTRIM(code))) HAVING COUNT(*)>1)
                    THROW 51002,'Legacy coupon codes collide after normalization. Review before migrating.',1;
                IF EXISTS (SELECT 1 FROM Coupon c LEFT JOIN Product p ON p.id=c.productId LEFT JOIN [User] u ON u.id=p.sellerId
                    WHERE c.productId IS NOT NULL AND (p.sellerId IS NULL OR u.role<>N'seller' OR u.id IS NULL))
                    THROW 51003,'Legacy product coupon has no valid seller.',1;
                IF EXISTS (SELECT 1 FROM Coupon WHERE productId IS NULL) AND NOT EXISTS (SELECT 1 FROM [User] WHERE role=N'admin')
                    THROW 51004,'An admin account is required to own platform coupons.',1;
                IF EXISTS (SELECT 1 FROM OrderTable o WHERE o.CouponCode IS NOT NULL AND LTRIM(RTRIM(o.CouponCode))<>''
                    AND NOT EXISTS (SELECT 1 FROM Coupon c WHERE UPPER(LTRIM(RTRIM(c.code)))=UPPER(LTRIM(RTRIM(o.CouponCode)))))
                    THROW 51005,'Historical order coupon has no mapping. Review before migrating.',1;
                """);
            migrationBuilder.AddColumn<decimal>(
                name: "PlatformSubsidy",
                table: "OrderTable",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PricingFingerprint",
                table: "OrderTable",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricingSchemaVersion",
                table: "OrderTable",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerGoodsDiscount",
                table: "OrderTable",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerGrossSnapshot",
                table: "OrderTable",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingBase",
                table: "OrderTable",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ShippingDiscount",
                table: "OrderTable",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PlatformDiscountSnapshot",
                table: "OrderItem",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerDiscountSnapshot",
                table: "OrderItem",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Promotion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FundingSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SellerId = table.Column<int>(type: "int", nullable: true),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    LegacyCouponId = table.Column<int>(type: "int", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsPercent = table.Column<bool>(type: "bit", nullable: false),
                    FreeShipping = table.Column<bool>(type: "bit", nullable: false),
                    Cap = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MinSubtotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MinQuantity = table.Column<int>(type: "int", nullable: false),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPaused = table.Column<bool>(type: "bit", nullable: false),
                    AdminPaused = table.Column<bool>(type: "bit", nullable: false),
                    PauseReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    MaxUsage = table.Column<int>(type: "int", nullable: true),
                    MaxUsagePerBuyer = table.Column<int>(type: "int", nullable: true),
                    Budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promotion_User_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "User",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_Promotion_User_SellerId",
                        column: x => x.SellerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "OrderPromotionSnapshot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FundingSource = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AllocationJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPromotionSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderPromotionSnapshot_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_OrderPromotionSnapshot_Promotion_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotion",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PromotionAudit",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChangesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionAudit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionAudit_Promotion_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionAudit_User_ActorId",
                        column: x => x.ActorId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "PromotionTarget",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionTarget", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionTarget_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PromotionTarget_Product_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Product",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PromotionTarget_Promotion_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionTier",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    MinQuantity = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionTier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionTier_Promotion_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionUsage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PromotionId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    BuyerId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReservedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionUsage_OrderTable_OrderId",
                        column: x => x.OrderId,
                        principalTable: "OrderTable",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_PromotionUsage_Promotion_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotion",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PromotionUsage_User_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "User",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderPromotionSnapshot_OrderId_PromotionId",
                table: "OrderPromotionSnapshot",
                columns: new[] { "OrderId", "PromotionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderPromotionSnapshot_PromotionId",
                table: "OrderPromotionSnapshot",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotion_Code",
                table: "Promotion",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Promotion_CreatedById",
                table: "Promotion",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Promotion_LegacyCouponId",
                table: "Promotion",
                column: "LegacyCouponId",
                unique: true,
                filter: "[LegacyCouponId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Promotion_SellerId_Type_StartAt_EndAt",
                table: "Promotion",
                columns: new[] { "SellerId", "Type", "StartAt", "EndAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionAudit_ActorId",
                table: "PromotionAudit",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionAudit_PromotionId",
                table: "PromotionAudit",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTarget_CategoryId",
                table: "PromotionTarget",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTarget_ProductId",
                table: "PromotionTarget",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTarget_PromotionId",
                table: "PromotionTarget",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTier_PromotionId_MinQuantity",
                table: "PromotionTier",
                columns: new[] { "PromotionId", "MinQuantity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsage_BuyerId",
                table: "PromotionUsage",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsage_OrderId_PromotionId",
                table: "PromotionUsage",
                columns: new[] { "OrderId", "PromotionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionUsage_PromotionId_State_BuyerId",
                table: "PromotionUsage",
                columns: new[] { "PromotionId", "State", "BuyerId" });
            migrationBuilder.Sql("""
                INSERT INTO Promotion (Name,Type,FundingSource,SellerId,CreatedById,LegacyCouponId,Code,Value,IsPercent,FreeShipping,
                    MinSubtotal,MinQuantity,StartAt,EndAt,IsPaused,AdminPaused,Version,MaxUsage,CreatedAt,UpdatedAt)
                SELECT N'Coupon '+UPPER(LTRIM(RTRIM(c.code))),N'Coupon',CASE WHEN c.productId IS NULL THEN N'Platform' ELSE N'Seller' END,
                    CASE WHEN c.productId IS NULL THEN NULL ELSE p.sellerId END,
                    CASE WHEN c.productId IS NULL THEN (SELECT MIN(id) FROM [User] WHERE role=N'admin') ELSE p.sellerId END,
                    c.id,UPPER(LTRIM(RTRIM(c.code))),c.discountPercent,1,0,0,1,
                    COALESCE(c.startDate,CONVERT(datetime2,'2000-01-01')),COALESCE(c.endDate,CONVERT(datetime2,'9999-12-31')),0,0,1,c.maxUsage,SYSUTCDATETIME(),SYSUTCDATETIME()
                FROM Coupon c LEFT JOIN Product p ON p.id=c.productId
                WHERE NOT EXISTS (SELECT 1 FROM Promotion v WHERE v.LegacyCouponId=c.id);
                INSERT INTO PromotionTarget (PromotionId,ProductId)
                SELECT p.Id,c.productId FROM Promotion p JOIN Coupon c ON c.id=p.LegacyCouponId WHERE c.productId IS NOT NULL
                    AND NOT EXISTS(SELECT 1 FROM PromotionTarget t WHERE t.PromotionId=p.Id AND t.ProductId=c.productId);
                INSERT INTO PromotionUsage (PromotionId,OrderId,BuyerId,Amount,State,ReservedAt,ConsumedAt)
                SELECT p.Id,o.id,o.buyerId,o.DiscountAmount,
                    CASE WHEN EXISTS(SELECT 1 FROM Payment pay WHERE pay.orderId=o.id AND pay.status=N'Succeeded') THEN N'Consumed' ELSE N'Reserved' END,
                    COALESCE(o.orderDate,SYSUTCDATETIME()),
                    (SELECT MIN(pay.PaidAt) FROM Payment pay WHERE pay.orderId=o.id AND pay.status=N'Succeeded')
                FROM OrderTable o JOIN Promotion p ON p.Code=UPPER(LTRIM(RTRIM(o.CouponCode)))
                WHERE o.buyerId IS NOT NULL
                    AND (EXISTS(SELECT 1 FROM Payment pay WHERE pay.orderId=o.id AND pay.status=N'Succeeded') OR o.status=N'AwaitingPayment')
                    AND NOT EXISTS(SELECT 1 FROM PromotionUsage u WHERE u.OrderId=o.id AND u.PromotionId=p.Id);
                INSERT INTO PromotionAudit (PromotionId,ActorId,Action,Reason,ChangesJson,CreatedAt)
                SELECT p.Id,p.CreatedById,N'Migrate',N'Chuyển coupon hiện có; giữ lượt dùng lịch sử.',N'{}',SYSUTCDATETIME()
                FROM Promotion p WHERE p.LegacyCouponId IS NOT NULL AND NOT EXISTS(SELECT 1 FROM PromotionAudit a WHERE a.PromotionId=p.Id AND a.Action=N'Migrate');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderPromotionSnapshot");

            migrationBuilder.DropTable(
                name: "PromotionAudit");

            migrationBuilder.DropTable(
                name: "PromotionTarget");

            migrationBuilder.DropTable(
                name: "PromotionTier");

            migrationBuilder.DropTable(
                name: "PromotionUsage");

            migrationBuilder.DropTable(
                name: "Promotion");

            migrationBuilder.DropColumn(
                name: "PlatformSubsidy",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "PricingFingerprint",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "PricingSchemaVersion",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "SellerGoodsDiscount",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "SellerGrossSnapshot",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "ShippingBase",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "ShippingDiscount",
                table: "OrderTable");

            migrationBuilder.DropColumn(
                name: "PlatformDiscountSnapshot",
                table: "OrderItem");

            migrationBuilder.DropColumn(
                name: "SellerDiscountSnapshot",
                table: "OrderItem");
        }
    }
}
