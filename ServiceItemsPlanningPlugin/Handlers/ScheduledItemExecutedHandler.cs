﻿/*
The MIT License (MIT)
Copyright (c) 2007 - 2019 Microting A/S
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:
The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ServiceItemsPlanningPlugin.Messages;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Constants;
using Microting.ItemsPlanningBase.Infrastructure.Data;
using Microting.ItemsPlanningBase.Infrastructure.Data.Entities;
using OpenStack.NetCoreSwiftClient.Extensions;
using Rebus.Bus;
using Rebus.Handlers;
using ServiceItemsPlanningPlugin.Infrastructure.Helpers;

namespace ServiceItemsPlanningPlugin.Handlers
{
    public class ScheduledItemExecutedHandler : IHandleMessages<ScheduledItemExecuted>
    {
        private readonly ItemsPlanningPnDbContext _dbContext;
        private readonly eFormCore.Core _sdkCore;
        private readonly IBus _bus;

        public ScheduledItemExecutedHandler(eFormCore.Core sdkCore, DbContextHelper dbContextHelper, IBus bus)
        {
            _sdkCore = sdkCore;
            _dbContext = dbContextHelper.GetDbContext();
            _bus = bus;
        }

        #pragma warning disable 1998
        public async Task Handle(ScheduledItemExecuted message)
        {
            var siteIds = _dbContext.PluginConfigurationValues.FirstOrDefault(x => x.Name == "ItemsPlanningBaseSettings:SiteIds");
            var list = await _dbContext.ItemLists.SingleOrDefaultAsync(x => x.Id == message.itemListId);
            var mainElement = _sdkCore.TemplateRead(list.RelatedEFormId);
            string folderId = getFolderId(list.Name).ToString();

            if (siteIds == null || siteIds.Value.IsNullOrEmpty())
            {
                Console.WriteLine("SiteIds not set");
                return;
            }

            Console.WriteLine($"SiteIds {siteIds}");

            foreach (var item in list.Items)
            {
                await _bus.SendLocal(new ItemCaseCreate(list.Id, item.Id, list.RelatedEFormId, list.Name));
            }
        }

        private int getFolderId(string name)
        {
            List<FolderDto> folderDtos = _sdkCore.FolderGetAll(true).Result;

            bool folderAlreadyExist = false;
            int microtingUId = 0;
            foreach (FolderDto folderDto in folderDtos)
            {
                if (folderDto.Name == name)
                {
                    folderAlreadyExist = true;
                    microtingUId = (int)folderDto.MicrotingUId;
                }
            }

            if (!folderAlreadyExist)
            {
                _sdkCore.FolderCreate(name, "", null);
                folderDtos = _sdkCore.FolderGetAll(true).Result;
                
                foreach (FolderDto folderDto in folderDtos)
                {
                    if (folderDto.Name == name)
                    {
                        microtingUId = (int)folderDto.MicrotingUId;
                    }
                }
            }

            return microtingUId;
        }
    }
}
