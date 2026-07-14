using System.Text;

namespace OpenCode.Workspace.Core.Generation;

public static class OracleSqlExecutionScriptSupport
{
    public const string ResultBeginMarker = "__OPENCODE_RESULT_BEGIN__";
    public const string ResultEndMarker = "__OPENCODE_RESULT_END__";

    public static string NormalizeScriptText(string content)
        => NormalizeLineEndings(content);

    public static string NormalizeSingleStatementText(string content)
    {
        var normalized = NormalizeLineEndings(content).TrimEnd();
        if (normalized.EndsWith(";", StringComparison.Ordinal) || normalized.EndsWith("/", StringComparison.Ordinal))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        return normalized + "\n";
    }

    public static string BuildDiagnosticPreview(string content)
    {
        var builder = new StringBuilder();
        foreach (var rawLine in NormalizeLineEndings(content).Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith("-- GENERATED FILE", StringComparison.Ordinal)
                || line.StartsWith("-- Source inputs:", StringComparison.Ordinal)
                || line.StartsWith("-- User edits", StringComparison.Ordinal))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(line);
            if (builder.Length >= 240)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }

    public static string BuildShellLibrary()
        => """
oracle_sql_normalize_file() {
  local input_file=$1
  local output_file=$2
  perl -0pe 's/^\x{FEFF}//; s/\r\n?/\n/g' "$input_file" >"$output_file"
}

oracle_sql_normalize_single_statement_file() {
  local input_file=$1
  local output_file=$2
  perl -0pe 's/^\x{FEFF}//; s/\r\n?/\n/g; s/\s+\z/\n/s; s/(?:;|\/)\s*\z/\n/s' "$input_file" >"$output_file"
}

oracle_sql_preview_file() {
  local input_file=$1
  awk '
    {
      line=$0
      sub(/^[[:space:]]+/, "", line)
      sub(/[[:space:]]+$/, "", line)
      if (line == "" || line ~ /^-- GENERATED FILE/ || line ~ /^-- Source inputs:/ || line ~ /^-- User edits/) {
        next
      }
      printf "%s ", line
      count++
      if (count >= 6) {
        exit
      }
    }
  ' "$input_file" | xargs || true
}

oracle_sql_sanitize_output() {
  local input_file=$1
  sed -E 's#([A-Za-z0-9_]+)/[^[:space:]@]+@//[^[:space:]]+#\1/[redacted]@//[redacted]#g; s#(IDENTIFIED BY )"[^"]+"#\1"[redacted]"#g' "$input_file"
}

oracle_sql_extract_result() {
  local input_file=$1
  awk '
    $0 == "__OPENCODE_RESULT_BEGIN__" { capture=1; next }
    $0 == "__OPENCODE_RESULT_END__" { capture=0; exit }
    capture { print }
  ' "$input_file"
}

oracle_sql_write_wrapper() {
  local mode=$1
  local normalized_file=$2
  local wrapper_file=$3
  cat >"$wrapper_file" <<'SQL'
WHENEVER OSERROR EXIT FAILURE
WHENEVER SQLERROR EXIT SQL.SQLCODE
SQL
  case "$mode" in
    single-sql-statement)
      cat >>"$wrapper_file" <<'SQL'
SET PAGESIZE 0
SET FEEDBACK OFF
SET HEADING OFF
SET VERIFY OFF
SET ECHO OFF
SET TRIMSPOOL ON
SQL
      printf 'PROMPT __OPENCODE_RESULT_BEGIN__\n' >>"$wrapper_file"
      cat "$normalized_file" >>"$wrapper_file"
      printf ';\n' >>"$wrapper_file"
      printf 'PROMPT __OPENCODE_RESULT_END__\n' >>"$wrapper_file"
      ;;
    plsql-block)
      cat "$normalized_file" >>"$wrapper_file"
      printf '\n' >>"$wrapper_file"
      ;;
    query-script)
      cat "$normalized_file" >>"$wrapper_file"
      ;;
    script|sqlcl-command-script)
      printf '@%s\n' "$normalized_file" >>"$wrapper_file"
      ;;
    *)
      printf 'Unsupported Oracle SQL execution mode: %s\n' "$mode" >&2
      return 1
      ;;
  esac
  printf 'EXIT\n' >>"$wrapper_file"
}

oracle_sql_report_failure() {
  local phase=$1
  local client=$2
  local mode=$3
  local source_id=$4
  local preview=$5
  local exit_code=$6
  local output_file=$7
  printf '[oracle-sql] phase=%s\n' "$phase" >&2
  printf '[oracle-sql] client=%s\n' "$client" >&2
  printf '[oracle-sql] mode=%s\n' "$mode" >&2
  printf '[oracle-sql] source=%s\n' "$source_id" >&2
  printf '[oracle-sql] statement=%s\n' "${preview:-<empty>}" >&2
  printf '[oracle-sql] exit_code=%s\n' "$exit_code" >&2
  printf '[oracle-sql] output_begin\n' >&2
  oracle_sql_sanitize_output "$output_file" >&2 || true
  printf '[oracle-sql] output_end\n' >&2
}

oracle_sql_run_file() {
  local phase=$1
  local client=$2
  local connection=$3
  local mode=$4
  local source_id=$5
  local input_file=$6
  local work_dir normalized_file wrapper_file output_file preview exit_code
  work_dir=$(mktemp -d)
  normalized_file="$work_dir/input.sql"
  wrapper_file="$work_dir/wrapper.sql"
  output_file="$work_dir/output.txt"

  if [ "$mode" = 'single-sql-statement' ]; then
    oracle_sql_normalize_single_statement_file "$input_file" "$normalized_file"
  else
    oracle_sql_normalize_file "$input_file" "$normalized_file"
  fi

  oracle_sql_write_wrapper "$mode" "$normalized_file" "$wrapper_file"

  if [ "$client" = 'sqlplus' ]; then
    sqlplus -L -S "$connection" @"$wrapper_file" >"$output_file" 2>&1
    exit_code=$?
  else
    sql -S "$connection" @"$wrapper_file" >"$output_file" 2>&1
    exit_code=$?
  fi

  if [ "$exit_code" -ne 0 ]; then
    cat "$output_file"
    preview=$(oracle_sql_preview_file "$normalized_file")
    oracle_sql_report_failure "$phase" "$client" "$mode" "$source_id" "$preview" "$exit_code" "$output_file"
  elif [ "$mode" = 'single-sql-statement' ] || [ "$mode" = 'query-script' ]; then
    oracle_sql_extract_result "$output_file"
  else
    cat "$output_file"
  fi

  rm -rf "$work_dir"
  return "$exit_code"
}
""";

    private static string NormalizeLineEndings(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        if (normalized.Length > 0 && normalized[0] == '\uFEFF')
        {
            normalized = normalized[1..];
        }

        return normalized;
    }
}
