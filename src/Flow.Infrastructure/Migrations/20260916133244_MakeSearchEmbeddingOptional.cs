using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Flow.Infrastructure.Migrations
{
    /// <summary>
    /// Вектор чанка становится необязательным: при Search:Embeddings:Enabled=false индексация пишет
    /// чанки без него, и текст остаётся находимым полнотекстовой половиной без единой модели.
    /// Откат возможен, только пока таких строк нет: NOT NULL на столбце с NULL'ами БД не поставит.
    /// </summary>
    public partial class MakeSearchEmbeddingOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<HalfVector>(
                name: "Embedding",
                table: "SearchChunks",
                type: "halfvec(512)",
                nullable: true,
                oldClrType: typeof(HalfVector),
                oldType: "halfvec(512)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<HalfVector>(
                name: "Embedding",
                table: "SearchChunks",
                type: "halfvec(512)",
                nullable: false,
                oldClrType: typeof(HalfVector),
                oldType: "halfvec(512)",
                oldNullable: true);
        }
    }
}
