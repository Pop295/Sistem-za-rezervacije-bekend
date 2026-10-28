# Sistem za rezervacije – Backend

REST API za sistem rezervacije mesta u restoranu. Backend je razvijen u ASP.NET Core-u (.NET 8) i predstavlja serversku stranu aplikacije. Frontend se nalazi u posebnom repozitorijumu: [Sistem-za-rezervacije-frontend](https://github.com/Pop295/Sistem-za-rezervacije-frontend).

## Funkcionalnosti

- Registracija i prijava korisnika (JWT autentifikacija i autorizacija po ulogama)
- Bezbedno čuvanje lozinki (BCrypt) i promena lozinke
- Upravljanje uslugama, terminima i rezervacijama (CRUD)
- Validacija rezervacija (npr. zabrana rezervacije u prošlosti)
- Notifikacije o kreiranim rezervacijama
- Pozadinski servis koji automatski šalje podsetnike pre rezervacije
- Swagger dokumentacija API-ja

## Tehnologije

- C#, ASP.NET Core Web API (.NET 8)
- Entity Framework Core (Database-First, Pomelo provider)
- MySQL / MariaDB
- JWT (JwtBearer), BCrypt.Net
- Swagger (Swashbuckle)

## Struktura baze

Glavni entiteti: `Role`, `User`, `Service`, `TimeSlot`, `Reservation`, `Notification`.

## Pokretanje

### Preduslovi
- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- MySQL ili MariaDB server

### Koraci

1. Kloniraj repozitorijum:
   ```bash
   git clone https://github.com/Pop295/Sistem-za-rezervacije-bekend.git
   cd Sistem-za-rezervacije-bekend
   ```
2. Kreiraj bazu i importuj šemu: `<putanja do SQL skripte, npr. database/schema.sql>`
3. Podesi konekciju i JWT ključ u `appsettings.json` (ili u `appsettings.Development.json`):
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Database=<ime_baze>;User=<korisnik>;Password=<lozinka>;"
     },
     "Jwt": {
       "Key": "<tajni_kljuc>",
       "Issuer": "<issuer>",
       "Audience": "<audience>"
     }
   }
   ```
4. Pokreni aplikaciju:
   ```bash
   dotnet restore
   dotnet run
   ```
5. Swagger je dostupan na: `https://localhost:<port>/swagger`

## API pregled

| Kontroler | Opis |
|-----------|------|
| `AuthController` | Prijava, registracija, promena lozinke |
| `UsersController` | Upravljanje korisnicima |
| `ServicesController` | Upravljanje uslugama |
| `TimeSlotsController` | Upravljanje terminima |
| `ReservationsController` | Kreiranje i upravljanje rezervacijama |
| `NotificationsController` | Notifikacije korisnika |
