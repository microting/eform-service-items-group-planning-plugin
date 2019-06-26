﻿namespace ServiceItemsPlanningPlugin.Scheduler.Jobs
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Messages;
    using Microsoft.EntityFrameworkCore;
    using Microting.ItemsPlanningBase.Infrastructure.Data;
    using Microting.ItemsPlanningBase.Infrastructure.Enums;
    using Rebus.Bus;

    public class SearchListJob : IJob
    {
        private readonly ItemsPlanningPnDbContext _dbContext;
        private readonly IBus _bus;

        public SearchListJob(ItemsPlanningPnDbContext dbContext, IBus bus)
        {
            _dbContext = dbContext;
            _bus = bus;
        }

        public async Task Execute()
        {
            Console.WriteLine("SearchListJob.Execute got called");
            var now = DateTime.UtcNow;
            var lastDayOfMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1).AddDays(-1).Day;
            var scheduledItemLists = await _dbContext.ItemLists
                .Where(x =>
                    (x.RepeatUntil == null || DateTime.UtcNow <= x.RepeatUntil) 
                    && 
                    (
                        x.LastExecutedTime == null ||

                        x.RepeatType == RepeatType.Day 
                           && now.AddDays(-x.RepeatEvery) >= x.LastExecutedTime ||

                        x.RepeatType == RepeatType.Week 
                           && x.DayOfWeek == now.DayOfWeek 
                           && now.AddDays(-x.RepeatEvery * 7) >= x.LastExecutedTime ||

                        x.RepeatType == RepeatType.Month 
                           && (x.DayOfMonth <= now.Day || now.Day == lastDayOfMonth)
                           && (now.AddMonths(-x.RepeatEvery).Month >= x.LastExecutedTime.Value.Month || now.AddMonths(-x.RepeatEvery).Year > x.LastExecutedTime.Value.Year)
                     )
                ).ToListAsync();
            
            Console.WriteLine($"We found the following number of entries: {scheduledItemLists.Count}");

            Console.WriteLine("SearchListJob executed");

            foreach (var list in scheduledItemLists)
            {
                list.LastExecutedTime = now;
                await list.Update(_dbContext);

                await _bus.SendLocal(new ScheduledItemExecuted(list.Id));

                Console.WriteLine($"List {list.Name} executed");
            }
        }
    }
}