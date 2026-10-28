# Sistem za rezervacije – Backend

REST API za sistem rezervacije mesta u restoranu, razvijen u ASP.NET Core-u (.NET 8). Predstavlja serversku stranu aplikacije. Frontend se nalazi u posebnom repozitorijumu: [Sistem-za-rezervacije-frontend](https://github.com/Pop295/Sistem-za-rezervacije-frontend).

## Funkcionalnosti

- Registracija i prijava korisnika uz JWT autentifikaciju (token važi 2 sata)
- Autorizacija po ulogama (`korisnik`, `admin`)
- Bezbedno čuvanje lozinki (BCrypt) i promena lozinke
- Upravljanje uslugama, terminima (stolovima) i rezervacijama
- Javni pregled aktivnih usluga i zauzetosti termina (bez prijave)
- Statusi rezervacije: `pending`, `confirmed`, `cancelled`, `completed`
- Notifikacije korisniku pri kreiranju rezervacije
- Pozadinski servis (`BackgroundService`) koji na svakih 5 minuta šalje podsetnik za rezervacije koje počinju za manje od 12 sati
- Admin funkcije: upravljanje korisnicima i ulogama, uslugama, terminima i statusima rezervacija
- Swagger dokumentacija sa podrškom za JWT (Bearer) autorizaciju

## Tehnologije

- C#, ASP.NET Core Web API (.NET 8)
- Entity Framework Core 9 (Database-First) + Pomelo MySQL provider
- MySQL / MariaDB
- JWT Bearer autentifikacija, BCrypt.Net
- Swagger (Swashbuckle)

## Struktura projekta

```
Controllers/   API kontroleri (Auth, Users, Services, TimeSlots, Reservations, Notifications)
DTOs/          Objekti za prenos podataka (zahtevi i odgovori)
Models/        Entiteti generisani iz baze (Database-First)
Data/          AppDbContext
Services/      ReservationReminderService (pozadinski servis)
Program.cs     Konfiguracija aplikacije (JWT, CORS, Swagger, EF Core)
```

## Baza podataka

Entiteti: `Role`, `User`, `Service`, `TimeSlot`, `Reservation`, `Notification`.

- Jedan termin (`TimeSlot`) može imati najviše jednu rezervaciju (jedinstveni indeks na `time_slot_id`).
- Pri registraciji korisnik dobija ulogu sa `id = 1` (`korisnik`), pa tabela `roles` mora sadržati uloge pre prvog pokretanja (`korisnik` sa id 1 i `admin`).

## Pokretanje

### Preduslovi

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- MySQL ili MariaDB server

### Koraci

1. Kloniraj repozitorijum:
   ```bash
   git clone https://github.com/Pop295/Sistem-za-rezervacije-bekend.git
   cd Sistem-za-rezervacije-bekend
   ```
2. Kreiraj bazu `reservation_system` i importuj šemu:
   ```bash
   mysql -u root -p -e "CREATE DATABASE reservation_system CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
   mysql -u root -p reservation_system < database/schema.sql
   ```
3. Podesi konekciju i JWT u `appsettings.json` (vrednosti u repozitorijumu su samo primer za lokalni razvoj):
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "server=localhost;port=3306;database=reservation_system;user=<korisnik>;password=<lozinka>"
     },
     "Jwt": {
       "Key": "<dug-tajni-kljuc>",
       "Issuer": "SistemZaRezervacije",
       "Audience": "SistemZaRezervacijeUsers"
     }
   }
   ```
4. Pokreni aplikaciju:
   ```bash
   dotnet restore
   dotnet run --launch-profile http
   ```
5. API radi na `http://localhost:5127`, a Swagger je dostupan na `http://localhost:5127/swagger`.

> Frontend očekuje backend na `http://localhost:5127`, a CORS je podešen za `http://localhost:5173` (Vite dev server).

## API pregled

| Kontroler | Ruta | Pristup |
|-----------|------|---------|
| `AuthController` | `POST /api/auth/register`, `POST /api/auth/login` | javno |
| | `GET /api/auth/me`, `PUT /api/auth/change-password` | prijavljen korisnik |
| `ServicesController` | `GET /api/services`, `GET /api/services/{id}` | javno |
| | `POST`, `PUT /{id}`, `DELETE /{id}` | admin |
| `TimeSlotsController` | `GET /api/timeslots` | javno |
| | `POST`, `PUT /{id}`, `DELETE /{id}` | admin |
| `ReservationsController` | `GET`, `POST`, `DELETE /{id}` | prijavljen korisnik |
| | `PUT /api/reservations/{id}/status` | admin |
| `NotificationsController` | `GET`, `PUT /{id}/read`, `DELETE /{id}` | prijavljen korisnik |
| `UsersController` | `GET`, `GET /{id}`, `PUT /{id}/role`, `DELETE /{id}` | admin |

Detaljna dokumentacija svih ruta dostupna je u Swagger-u.

## Autori

- Backend: [@Pop295](https://github.com/Pop295)
- Frontend: [ISSAmaci](https://github.com/ISSAmaci)
