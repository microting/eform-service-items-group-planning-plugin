using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Dto;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.ItemsGroupPlanningBase.Infrastructure.Data;
using Microting.ItemsGroupPlanningBase.Infrastructure.Data.Entities;
using Rebus.Handlers;
using ServiceItemsGroupPlanningPlugin.Infrastructure.Helpers;
using ServiceItemsGroupPlanningPlugin.Messages;

namespace ServiceItemsGroupPlanningPlugin.Handlers
{
    public class ItemCaseCreateHandler : IHandleMessages<ItemCaseCreate>
    {
        private readonly ItemsGroupPlanningPnDbContext _dbContext;
        private readonly eFormCore.Core _sdkCore;

        public ItemCaseCreateHandler(eFormCore.Core sdkCore, DbContextHelper dbContextHelper)
        {
            _sdkCore = sdkCore;
            _dbContext = dbContextHelper.GetDbContext();
        }

        public async Task Handle(ItemCaseCreate message)
        {
            Item item = await _dbContext.Items.SingleOrDefaultAsync(x => x.Id == message.ItemId);

            if (item != null)
            {
                var siteIds = await _dbContext.PluginConfigurationValues.FirstOrDefaultAsync(x => x.Name == "ItemsPlanningBaseSettings:SiteIds");

                Language language = await _sdkCore.DbContextHelper.GetDbContext().Languages
                    .SingleAsync(x => x.LanguageCode == "da");
                var mainElement = _sdkCore.ReadeForm(message.RelatedEFormId, language);
                string folderId = GetFolderId(message.Name).ToString();

                ItemCase itemCase = await _dbContext.ItemCases.SingleOrDefaultAsync(x => x.ItemId == item.Id && x.WorkflowState != Constants.WorkflowStates.Retracted);
                if (itemCase != null)
                {
                    itemCase.WorkflowState = Constants.WorkflowStates.Retracted;
                    await itemCase.Update(_dbContext);
                }

                itemCase = new ItemCase
                {
                    ItemId = item.Id,
                    Status = 66,
                    MicrotingSdkeFormId = message.RelatedEFormId
                };
                await itemCase.Create(_dbContext);

                foreach (var siteIdString in siteIds.Value.Split(','))
                {
                    var siteId = int.Parse(siteIdString);
                    var casesToDelete = _dbContext.ItemCaseSites.
                        Where(x => x.ItemId == item.Id && x.MicrotingSdkSiteId == siteId && x.WorkflowState != Constants.WorkflowStates.Retracted);

                    foreach (ItemCaseSite caseToDelete in casesToDelete)
                    {
                        CaseDto caseDto = await _sdkCore.CaseLookupCaseId(caseToDelete.MicrotingSdkCaseId);
                        if (caseDto.MicrotingUId != null) await _sdkCore.CaseDelete((int) caseDto.MicrotingUId);
                        caseToDelete.WorkflowState = Constants.WorkflowStates.Retracted;
                        await caseToDelete.Update(_dbContext);
                    }

                    mainElement.Result.Label = string.IsNullOrEmpty(item.ItemNumber) ? "" : item.ItemNumber;
                    if (!string.IsNullOrEmpty(item.Name))
                    {
                        mainElement.Result.Label += string.IsNullOrEmpty(mainElement.Result.Label) ? $"{item.Name}" : $" - {item.Name}";
                    }

                    if (!string.IsNullOrEmpty(item.BuildYear))
                    {
                        mainElement.Result.Label += string.IsNullOrEmpty(mainElement.Result.Label) ? $"{item.BuildYear}" : $" - {item.BuildYear}";
                    }

                    if (!string.IsNullOrEmpty(item.Type))
                    {
                        mainElement.Result.Label += string.IsNullOrEmpty(mainElement.Result.Label) ? $"{item.Type}" : $" - {item.Type}";
                    }
                    mainElement.Result.ElementList[0].Label = mainElement.Result.Label;
                    mainElement.Result.CheckListFolderName = folderId;
                    mainElement.Result.StartDate = DateTime.Now.ToUniversalTime();
                    mainElement.Result.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();

                    ItemCaseSite itemCaseSite =
                        await _dbContext.ItemCaseSites.SingleOrDefaultAsync(x => x.ItemCaseId == itemCase.Id && x.MicrotingSdkSiteId == siteId);

                    if (itemCaseSite == null)
                    {
                        itemCaseSite = new ItemCaseSite
                        {
                            MicrotingSdkSiteId = siteId,
                            MicrotingSdkeFormId = message.RelatedEFormId,
                            Status = 66,
                            ItemId = item.Id,
                            ItemCaseId = itemCase.Id
                        };

                        await itemCaseSite.Create(_dbContext);
                    }

                    if (itemCaseSite.MicrotingSdkCaseId >= 1) continue;
                    int? caseId = await _sdkCore.CaseCreate(await mainElement, "", siteId, null);
                    if (caseId != null)
                    {
                        CaseDto caseDto = await _sdkCore.CaseLookupMUId((int) caseId);
                        if (caseDto?.CaseId != null) itemCaseSite.MicrotingSdkCaseId = (int) caseDto.CaseId;
                        await itemCaseSite.Update(_dbContext);
                    }
                }
            }
        }

        private int GetFolderId(string name)
        {
            List<FolderDto> folderDtos = _sdkCore.FolderGetAll(true).Result;

            bool folderAlreadyExist = false;
            int microtingUId = 0;
            foreach (FolderDto folderDto in folderDtos)
            {
                if (folderDto.Name == name)
                {
                    folderAlreadyExist = true;
                    if (folderDto.MicrotingUId != null) microtingUId = (int) folderDto.MicrotingUId;
                }
            }

            if (!folderAlreadyExist)
            {
                List<KeyValuePair<string, string>> names =
                    new List<KeyValuePair<string, string>>
                    {
                        new("da", name)
                    };

                List<KeyValuePair<string, string>> descriptions =
                    new List<KeyValuePair<string, string>>
                    {
                        new("da", "")
                    };
                _sdkCore.FolderCreate(names,descriptions , null);
                folderDtos = _sdkCore.FolderGetAll(true).Result;

                foreach (FolderDto folderDto in folderDtos)
                {
                    if (folderDto.Name == name)
                    {
                        if (folderDto.MicrotingUId != null) microtingUId = (int) folderDto.MicrotingUId;
                    }
                }
            }

            return microtingUId;
        }
    }
}