using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence;

/// <summary>
/// SQL-функции чтения пользовательских полей из jsonb (создаются миграцией AddCustomFields) — через них
/// TaskFilterTranslator строит условия `cf.*` выражениями, без сырого SQL. Вне запроса EF не вызываются.
/// Функции IMMUTABLE и берут значение одного поля по Id: число и дата приводятся, только если в jsonb лежит
/// значение нужного вида, иначе NULL — битое значение не роняет запрос.
/// </summary>
public static class CustomFieldSql
{
    /// <summary>Текст значения (строка, Id варианта, true/false) или NULL.</summary>
    public static string? Text(string customFields, string fieldId) => throw OnlyInQuery();

    public static decimal? Number(string customFields, string fieldId) => throw OnlyInQuery();

    public static DateOnly? Date(string customFields, string fieldId) => throw OnlyInQuery();

    /// <summary>Значение (строка или элемент массива) входит в <paramref name="values"/>.</summary>
    public static bool Any(string customFields, string fieldId, string[] values) => throw OnlyInQuery();

    internal static void Register(ModelBuilder modelBuilder)
    {
        void Map(string method, string name)
        {
            var function = modelBuilder.HasDbFunction(typeof(CustomFieldSql).GetMethod(method)!).HasName(name).HasSchema("public");
            function.HasParameter("customFields").HasStoreType("jsonb");
        }

        Map(nameof(Text), "flow_cf_text");
        Map(nameof(Number), "flow_cf_number");
        Map(nameof(Date), "flow_cf_date");
        Map(nameof(Any), "flow_cf_any");
    }

    /// <summary>Тела функций — для миграции AddCustomFields.</summary>
    internal const string CreateFunctionsSql = """
        CREATE OR REPLACE FUNCTION public.flow_cf_text(custom_fields jsonb, field_id text) RETURNS text
            LANGUAGE sql IMMUTABLE PARALLEL SAFE
            AS $$ SELECT CASE jsonb_typeof(custom_fields -> field_id) WHEN 'string' THEN custom_fields ->> field_id
                                                                      WHEN 'boolean' THEN custom_fields ->> field_id END $$;
        CREATE OR REPLACE FUNCTION public.flow_cf_number(custom_fields jsonb, field_id text) RETURNS numeric
            LANGUAGE sql IMMUTABLE PARALLEL SAFE
            AS $$ SELECT CASE WHEN jsonb_typeof(custom_fields -> field_id) = 'number' THEN (custom_fields ->> field_id)::numeric END $$;
        CREATE OR REPLACE FUNCTION public.flow_cf_date(custom_fields jsonb, field_id text) RETURNS date
            LANGUAGE sql IMMUTABLE PARALLEL SAFE
            AS $$ SELECT CASE WHEN (custom_fields ->> field_id) ~ '^\d{4}-\d{2}-\d{2}$' THEN (custom_fields ->> field_id)::date END $$;
        CREATE OR REPLACE FUNCTION public.flow_cf_any(custom_fields jsonb, field_id text, candidates text[]) RETURNS boolean
            LANGUAGE sql IMMUTABLE PARALLEL SAFE
            AS $$ SELECT CASE jsonb_typeof(custom_fields -> field_id)
                            WHEN 'array' THEN (custom_fields -> field_id) ?| candidates
                            WHEN 'string' THEN (custom_fields ->> field_id) = ANY (candidates)
                            ELSE false END $$;
        """;

    internal const string DropFunctionsSql = """
        DROP FUNCTION IF EXISTS public.flow_cf_text(jsonb, text);
        DROP FUNCTION IF EXISTS public.flow_cf_number(jsonb, text);
        DROP FUNCTION IF EXISTS public.flow_cf_date(jsonb, text);
        DROP FUNCTION IF EXISTS public.flow_cf_any(jsonb, text, text[]);
        """;

    private static InvalidOperationException OnlyInQuery() => new("CustomFieldSql functions are translated to SQL and cannot be called directly.");
}
