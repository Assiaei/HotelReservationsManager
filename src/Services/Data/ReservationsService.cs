using Data;
using Data.Models;
using Microsoft.EntityFrameworkCore;
using Services.Common;
using Services.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services.Data
{
    // For Doing Reservation
    public class ReservationsService : IReservationService
    {
    // *******************************************************************
        // For Injecting Dependencies
        private readonly ApplicationDbContext dbContext;
        private readonly ISettingService settingService;

        public ReservationsService(
            ApplicationDbContext dbContext, ISettingService settingService)
        {
            this.dbContext = dbContext;
            this.settingService = settingService;
        }
    // *******************************************************************

        
        /// <summary>
        /// Checks if the dates for a reservation are valid and if the room is free in that period
        /// </summary>
        /// <param name="roomId">Room's id</param>
        /// <param name="accomodationDate">Reservation's accomodation date</param>
        /// <param name="releaseDate">Reservation's release date</param>
        /// <param name="reservationId">Reservations to update id or null if making new reservation</param>
        /// <returns>Task with room's dates for reservatioin validity result</returns>

        // For Chacking The Room Is Acceptable In Dates Period
        public async Task<bool> AreDatesAcceptable(
            string roomId,
            DateTime accomodationDate,
            DateTime releaseDate,
            string reservationId = null)
        {
            // For Bad Date Period
            if (accomodationDate >= releaseDate || accomodationDate < DateTime.Today)
            {
                return false;
            }

            // For Getting Reservation Periods Tuple Of Room
            var reservationPeriods = await dbContext.Reservations.AsNoTracking().
               Where(x => x.Room.Id == roomId).
               Select(x => new Tuple<DateTime, DateTime>
                            (x.AccommodationDate, x.ReleaseDate).
                            ToValueTuple()).
              ToListAsync();

            // For Updated Reservation
            if (!string.IsNullOrWhiteSpace(reservationId))
            {
                // For Getting Reservation Info
                var reservation = await dbContext.Reservations.AsNoTracking().FirstOrDefaultAsync(
                    x => x.Id == reservationId);

                // For Removing This Reservation From Reservation Periods
                reservationPeriods = reservationPeriods.Where(
                    x => x.Item1 != reservation.AccommodationDate &&
                    x.Item2 != reservation.ReleaseDate).ToList();
            }

            // For Checking Room Is Acceptable & Returning Result
            return !reservationPeriods.Any(x =>
                (x.Item1 >= accomodationDate && x.Item1 <= releaseDate) ||
                (x.Item2 > accomodationDate && x.Item2 <= releaseDate) ||
                (x.Item1 >= accomodationDate && x.Item2 <= releaseDate) ||
                (x.Item1 <= accomodationDate && x.Item2 >= releaseDate));
        }

        // *******************************************************************
        
        /// <summary>
        /// Calculates the reservation total price
        /// </summary>
        /// <param name="room">The reservation room</param>
        /// <param name="clients">The room clients</param>
        /// <param name="allInclusive">Reservation's order all inclusive</param>
        /// <param name="breakfast">Reservation's order breakfast</param>
        /// <returns>Task with the calculation result</returns>

        // For Calculating Total Room Price For Night
        private async Task<double> CalculatePriceForNight(
          Room room,
          IEnumerable<ClientData> clients,
          bool allInclusive,
          bool breakfast)
        {
            // For Getting List Of Guest
            clients = clients.ToList().Where(x => x.FullName != null);

            // For Calculating Price Based On Number Of Adult & Children Guests
            var price =
                clients.Count(x => x.IsAdult) * room.AdultPrice +
                clients.Count(x => !x.IsAdult) * room.ChildrenPrice +
                room.AdultPrice;

            // For Adding Additional Price For All Inclusive Condition
            if (allInclusive)
            {
                price += double.Parse((await settingService.GetAsync(
                    $"{nameof(Reservation.AllInclusive)}Price")).Value);
            }
            // For Adding Additional Price For Breakfast Condition
            else if (breakfast)
            {
                price += double.Parse((await settingService.GetAsync(
                    $"{nameof(Reservation.Breakfast)}Price")).Value);
            }

            // For Returning Calculated Price
            return price;
        }

        // *******************************************************************

        /// <summary>
        /// Add reservation to database
        /// </summary>
        /// <param name="roomId">The room id</param>
        /// <param name="accomodationDate">The reservation accomodation date</param>
        /// <param name="releaseDate">The reservation release date </param>
        /// <param name="allInclusive">Reservation's order all inclusive</param>
        /// <param name="breakfast">Reservation's order breakfast</param>
        /// <param name="clients">The room's clients</param>
        /// <param name="user">The room renter</param>
        /// <returns>Task with the new reservation result</returns>

        // For Adding Reservation
        public async Task<Reservation> AddReservation(
          string roomId,
          DateTime accomodationDate,
          DateTime releaseDate,
          bool allInclusive,
          bool breakfast,
          IEnumerable<ClientData> clients,
          ApplicationUser user)
        {
            // For Getting Room Info
            var room = await dbContext.Rooms.FindAsync(roomId);

            // For Bad Condition: Not Exist Room
            if (room == null)
            {
                return null;
            }

            // For Bad Condition: Not Acceptable Room On Dates
            if (!await AreDatesAcceptable(roomId, accomodationDate, releaseDate))
            {
                return null;
            }

            // For Bad Condition: Guest # Is Greater Than Room Capacity
            if (clients.Count() + 1 > room.Capacity)
            {
                return null;
            }

            // For Calculating Price
            var price = await CalculatePriceForNight(room, clients, allInclusive, breakfast) 
                * (releaseDate-accomodationDate).TotalDays;

            // For Creating Reservation
            var reservation = new Reservation
            {
                AccommodationDate = accomodationDate,
                AllInclusive = allInclusive,
                Breakfast = breakfast,
                Price = price,
                Room = room,
                ReleaseDate = releaseDate,
                Clients = clients,
                User = user,
            };

            // For Adding Reservation To Db & Saving Changes
            this.dbContext.Reservations.Add(reservation);
            await this.dbContext.SaveChangesAsync();

            // For Returning Reservation
            return reservation;
        }

        // *******************************************************************
        
        /// <summary>
        /// Update reservation data
        /// </summary>
        /// <param name="id">The reservation id</param>
        /// <param name="roomId">The room id</param>
        /// <param name="accomodationDate">The reservation accomodation date</param>
        /// <param name="releaseDate">The reservation release date </param>
        /// <param name="allInclusive">Reservation's order all inclusive</param>
        /// <param name="breakfast">Reservation's order breakfast</param>
        /// <param name="clients">The room's clients</param>
        /// <param name="user">The room renter</param>
        /// <returns>Task representing the success of the update operation</returns>

        // For Updating a Reservation
        public async Task<bool> UpdateReservation(string id,
                                                  DateTime accomodationDate,
                                                  DateTime releaseDate,
                                                  bool allInclusive,
                                                  bool breakfast,
                                                  IEnumerable<ClientData> clients,
                                                  ApplicationUser user)
        {
            // For Getting Reservation With User & Room InFo 
            var reservation = await dbContext.Reservations.AsNoTracking().Include(x => x.User)
                .Include(x=>x.Room).FirstOrDefaultAsync(x => x.Id == id);

            // For Getting Room InFo
            var room = await dbContext.Rooms.FirstOrDefaultAsync(
                x => x.Reservations.Any(y => y.Id == id));

            // For Checking Room Is Acceptable In New Dates Period
            var areDateAcceptable = await AreDatesAcceptable(
                room.Id, accomodationDate, releaseDate, id);

            // For Checking Guest # Is In Range Of Room Capacity
            var isCapacityInRange = clients.Count() + 1 <= room.Capacity;

            // For Checking User Can Update Reservation
            var isUserAuthorizedToUpdate = reservation.User.Id == user.Id ||
               dbContext.UserRoles.Any(x => x.UserId == user.Id &&
                   x.RoleId != dbContext.Roles.First(a => a.Name == "User").Id);

            // For Bad Condition Update
            if (!isUserAuthorizedToUpdate || !isCapacityInRange || !areDateAcceptable)
            {
                return false;
            }

            // For Calculating Reservation Price
            var price = await CalculatePriceForNight(room, clients, allInclusive, breakfast) * 
                (releaseDate - accomodationDate).TotalDays;

            // For Creating Updated Reservation
            var newReservation = new Reservation
            {
                Id = id,
                AccommodationDate = accomodationDate,
                AllInclusive = allInclusive,
                Breakfast = breakfast,
                Price = price,
                ReleaseDate = releaseDate,
                Room=room,
                Clients = clients,
                User = user
            };

            // For Updating Db & Saving Changes
            dbContext.Reservations.Update(newReservation);
            await this.dbContext.SaveChangesAsync();

            // For Returning Ok
            return true;
        }


        // *******************************************************************

        
        /// <summary>
        /// Removes reservation from the database
        /// </summary>
        /// <param name="id">The reservation to delete id</param>
        /// <returns>Task with the reservation deletion result</returns>

        // For Deleting a Reservation
        public async Task<bool> DeleteReservation(string id)
        {
            // For Getting Reservation
            var reservation = await this.dbContext.Reservations.FindAsync(id);

            // For Exist Reservation Doing
            if (reservation != null)
            {
                // For Removing Guests Info 
                this.dbContext.ClientData.RemoveRange(
                    this.dbContext.ClientData.Where(x => x.Reservation.Id == reservation.Id));

                // For Removing Reservation 
                this.dbContext.Reservations.Remove(reservation);

                // For Saving Changes In Db
                await this.dbContext.SaveChangesAsync();

                // For Returning Ok
                return true;
            }

            // For Returning Nok
            return false;
        }

        // *******************************************************************

        
        /// <summary>
        /// Finds reservation with the searched id
        /// </summary>
        /// <typeparam name="T">Data class to map reservation data to</typeparam>
        /// <param name="id">Reservation id to search for</param>
        /// <returns>Task with the reservation data parsed to <typeparamref name="T"/>
        /// object or null if not found</returns>

        // For Getting a Reservation
        public async Task<T> GetReservation<T>(string id)
        {
            return await this.dbContext.Reservations.AsNoTracking().Where(x => x.Id == id)
                .ProjectTo<T>().FirstOrDefaultAsync();
        }

        // *******************************************************************

        /// <summary>
        /// Finds the user's reservation
        /// </summary>
        /// <typeparam name="T">Data class to map reservation data to</typeparam>
        /// <param name="userId">The user who made the reservation id</param>
        /// <returns>Task with the reservations data parsed to <typeparamref name="T"/>
        /// object or null if not found</returns>

        // For Getting User's Reservations
        public async Task<IEnumerable<T>> GetReservationsForUser<T>(string userId)
        {
            return await this.dbContext.Reservations.AsNoTracking()
                .Where(x => x.User.Id == userId)
                .OrderByDescending(x => x.AccommodationDate)
                .ProjectTo<T>().ToListAsync();
        }

        // *******************************************************************

        
        /// <summary>
        /// Finds the user's reservations according to specified pagination rules
        /// </summary>
        /// <typeparam name="T">Data class to map reservation data to</typeparam>
        /// <param name="userId">The user who made the reservation id</param>
        /// <param name="page">The number of current page</param>
        /// <param name="elementsOnPage">The number of reservations on the page</param>
        /// <returns>Task with the reservations data parsed to <typeparamref name="T"/>
        /// object or null if not found</returns>

        // For Getting Paginated User's Reservations 
        public async Task<IEnumerable<T>> GetForUserOnPage<T>(string userId, int page, int elementsOnPage)
        {
            return await GetReservationsForUser<T>(userId).GetPageItems(page, elementsOnPage);
        }


        // *******************************************************************

        
        /// <summary>
        /// Verifies and updates clients data for existing reservation
        /// </summary>
        /// <param name="reservationId">Reservation id</param>
        /// <param name="clients">Updated clients list</param>
        /// <returns>Task with the clients data list result</returns>

        // For Updating Guests Info a Reservation
        public async Task<IEnumerable<ClientData>> UpdateClientsForReservation(
            string reservationId, IEnumerable<ClientData> clients)
        {
            // For Getting Reservation
            var reservation = await dbContext.Reservations.AsNoTracking()
              .Include(x => x.Room)
              .FirstOrDefaultAsync(x => x.Id == reservationId);

            // For Getting Current Guests Info Of Reservation
            var initialClients = await dbContext.ClientData
                .Where(x => x.Reservation.Id == reservationId)
                .ToListAsync();

            //if (clients.Count() + 1 > reservation.Room.Capacity) - Unnecessary

            // For Getting Removed Guests List
            var deletedClients = initialClients.Where(x => !clients.Select(u => u.Id).Contains(x.Id)).ToList();

            // For Exist Removed Guests 
            if (deletedClients?.Any() ?? false)
            {
                // For Removing Guests
                dbContext.ClientData.RemoveRange(deletedClients);
            }

            // For Getting New Added Guests List
            var newClients = clients.Where(x => !initialClients.Select(u => u.Id)
               .Contains(x.Id))
               .ToList();

            // For Exist New Added Guests
            if (newClients?.Any() ?? false)
            {
                // For Each Guest Setting ReservationId & Id
                foreach (var cl in newClients)
                {
                    cl.ReservationId = reservation.Id;
                    
                    if (string.IsNullOrWhiteSpace(cl.Id))
                    {
                        cl.Id = Guid.NewGuid().ToString();
                    }
                }

                // For Adding Guests To Db
                dbContext.ClientData.AddRange(newClients);
            }

            // For Getting Updated Guests List (Guest Which Is Not New & Has Id)
            var clientsToUpdate = clients.Where(x => !newClients.Select(u => u.Id).Contains(x.Id) 
                && x.Id != null).ToList();

            // For Exist Any In Updated Guests List
            if (clientsToUpdate?.Any() ?? false)
            {
                // For Each Guest Setting ReservationId
                foreach (var cl in newClients)
                {
                    cl.ReservationId = reservation.Id;
                }
                // For Adding Guests To Db
                dbContext.ClientData.UpdateRange(clientsToUpdate);
            }

            // For Saving Changes In Db
            await dbContext.SaveChangesAsync();

            // For Returning Guests List
            return clients;
        }

        // *******************************************************************

        /// <summary>
        /// Finds all rooms in the database
        /// </summary>
        /// <typeparam name="T">Data class to map room data to</typeparam>
        /// <returns>Task with all reservations data parsed to <typeparamref name="T"/>
        /// object or null if not found</returns>

        // For Getting Reservated Rooms List
        public async Task<IEnumerable<T>> GetAll<T>()
        {
            return await this.dbContext.Reservations.AsNoTracking()
                .OrderBy(x => x.ReleaseDate)
                .ProjectTo<T>()
                .ToListAsync();
        }


        // *******************************************************************

        
        /// <summary>
        /// Finds the count of all reservations in the database
        /// </summary>
        /// <returns>Task with the all reservations count result</returns>

        // For Counting All Reservations
        public async Task<int> CountAllReservations()
        {
            return await this.dbContext.Reservations.AsNoTracking().CountAsync();
        }

        // *******************************************************************
    }
}
