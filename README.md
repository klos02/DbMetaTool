# DbMetaTool

Narzędzie wiersza poleceń do zarządzania metadanymi i schematem baz danych Firebird 5.0.

## Opis

DbMetaTool umożliwia:

- **Budowanie nowych baz danych** z plików SQL (podejście infrastructure-as-code)
- **Eksport schematu bazy** do plików SQL (reverse engineering)
- **Aktualizację istniejących baz** za pomocą skryptów migracyjnych

Narzędzie wspiera zarówno wbudowany (embedded) silnik Firebird, jak i serwer Firebird uruchomiony w kontenerze Docker z automatycznym wykrywaniem trybu.

## Wymagania

- .NET 8.0 lub nowszy
- Firebird 5.0 (embedded lub server)

## Instalacja

```bash
# Klonowanie repozytorium
git clone https://github.com/your-repo/DbMetaTool.git
cd DbMetaTool

# Przywrócenie zależności i budowa
dotnet restore
dotnet build -c Release
```

## Komendy

### build-db

Tworzy nową bazę danych i wykonuje skrypty SQL.

```bash
DbMetaTool build-db --db-dir <ścieżka> --scripts-dir <ścieżka>
```

**Parametry:**
- `--db-dir` - katalog dla nowej bazy danych
  - Ścieżka zaczynająca się od `/` → serwer Docker (np. `/var/lib/firebird/data`)
  - Inna ścieżka → lokalna baza embedded
- `--scripts-dir` - katalog ze skryptami SQL do wykonania

**Przykład:**
```bash
# Baza embedded
DbMetaTool build-db --db-dir "C:\databases\mydb" --scripts-dir ".\scripts"

# Baza na serwerze Docker
DbMetaTool build-db --db-dir "/var/lib/firebird/data" --scripts-dir ".\scripts"
```

### export-scripts

Eksportuje schemat bazy danych do plików SQL.

```bash
DbMetaTool export-scripts --connection-string <connection-string> --output-dir <ścieżka>
```

**Parametry:**
- `--connection-string` - connection string do bazy Firebird
- `--output-dir` - katalog docelowy dla wyeksportowanych skryptów

**Wygenerowane pliki:**
- `01_domains.sql` - domeny (typy użytkownika)
- `02_tables.sql` - tabele z kolumnami i ograniczeniami
- `03_procedures.sql` - procedury składowane

**Przykład:**
```bash
DbMetaTool export-scripts \
  --connection-string "Host=localhost;Port=3050;Database=/var/lib/firebird/data/mydb.fdb;User=SYSDBA;Password=masterkey" \
  --output-dir ".\exported"
```

### update-db

Aktualizuje istniejącą bazę danych wykonując skrypty migracyjne.

```bash
DbMetaTool update-db --connection-string <connection-string> --scripts-dir <ścieżka>
```

**Parametry:**
- `--connection-string` - connection string do bazy Firebird
- `--scripts-dir` - katalog ze skryptami SQL do wykonania

**Przykład:**
```bash
DbMetaTool update-db \
  --connection-string "Host=localhost;Port=3050;Database=/var/lib/firebird/data/mydb.fdb;User=SYSDBA;Password=masterkey" \
  --scripts-dir ".\migrations"
```

## Sposób działania

### Kolejność wykonywania skryptów

Skrypty SQL są wykonywane w kolejności alfabetycznej według nazwy pliku. Zalecana konwencja nazewnictwa:

```
01_domains.sql
02_tables.sql
03_procedures.sql
10_table_users.sql
11_table_products.sql
20_procedure_get_user.sql
```

### Obsługa transakcji

- Każdy skrypt jest wykonywany w osobnej transakcji
- W przypadku błędu transakcja jest wycofywana (rollback)
- Błąd w jednym skrypcie nie zatrzymuje wykonania kolejnych
- Na końcu wyświetlane jest podsumowanie sukcesów i błędów

### Automatyczne wykrywanie trybu

Narzędzie automatycznie rozpoznaje tryb pracy:
- Ścieżka zaczynająca się od `/` → tryb serwera (Docker)
- Inna ścieżka → tryb embedded (lokalny)

## Konfiguracja Docker

W katalogu `development/` znajduje się plik `docker-compose.yml` do uruchomienia serwera Firebird:

```bash
cd development
docker-compose up -d
```

Domyślne ustawienia:
- Port: 3050
- Użytkownik: SYSDBA
- Hasło: masterkey

## Przykładowy workflow

```bash
# 1. Uruchom serwer Firebird (opcjonalnie)
cd development && docker-compose up -d && cd ..

# 2. Zbuduj nową bazę ze skryptów
DbMetaTool build-db --db-dir "/var/lib/firebird/data" --scripts-dir ".\development\scripts"

# 3. Wyeksportuj schemat do kontroli wersji
DbMetaTool export-scripts \
  --connection-string "Host=localhost;Port=3050;Database=/var/lib/firebird/data/database.fdb;User=SYSDBA;Password=masterkey" \
  --output-dir ".\schema"

# 4. Zastosuj migracje
DbMetaTool update-db \
  --connection-string "Host=localhost;Port=3050;Database=/var/lib/firebird/data/database.fdb;User=SYSDBA;Password=masterkey" \
  --scripts-dir ".\migrations"
```

## Struktura projektu

```
DbMetaTool/
├── DbMetaTool/
│   ├── Program.cs           # Główna logika aplikacji
│   ├── DbMetaTool.csproj    # Konfiguracja projektu
│   └── appsettings.json     # Domyślna konfiguracja
├── development/
│   ├── docker-compose.yml   # Konfiguracja Docker
│   ├── scripts/             # Przykładowe skrypty SQL
│   └── export/              # Przykładowe wyeksportowane skrypty
└── DbMetaTool.sln           # Solution Visual Studio
```
