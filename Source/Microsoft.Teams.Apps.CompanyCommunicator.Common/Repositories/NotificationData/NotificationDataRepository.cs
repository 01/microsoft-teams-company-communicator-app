﻿// <copyright file="NotificationDataRepository.cs" company="Microsoft">
// Copyright (c) Microsoft. All rights reserved.
// </copyright>

namespace Microsoft.Teams.Apps.CompanyCommunicator.Common.Repositories.NotificationData
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Repository of the notification data in the table storage.
    /// </summary>
    public class NotificationDataRepository : BaseRepository<NotificationDataEntity>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NotificationDataRepository"/> class.
        /// </summary>
        /// <param name="repositoryOptions">Options used to create the repository.</param>
        /// <param name="tableRowKeyGenerator">Table row key generator service.</param>
        public NotificationDataRepository(
            IOptions<RepositoryOptions> repositoryOptions,
            TableRowKeyGenerator tableRowKeyGenerator)
            : base(
                storageAccountConnectionString: repositoryOptions.Value.StorageAccountConnectionString,
                tableName: PartitionKeyNames.NotificationDataTable.TableName,
                defaultPartitionKey: PartitionKeyNames.NotificationDataTable.DraftNotificationsPartition,
                isExpectedTableAlreadyExist: repositoryOptions.Value.IsExpectedTableAlreadyExist)
        {
            this.TableRowKeyGenerator = tableRowKeyGenerator;
        }

        /// <summary>
        /// Gets table row key generator.
        /// </summary>
        public TableRowKeyGenerator TableRowKeyGenerator { get; }

        /// <summary>
        /// Get all draft notification entities from the table storage.
        /// </summary>
        /// <returns>All draft notification entities.</returns>
        public async Task<IEnumerable<NotificationDataEntity>> GetAllDraftNotificationsAsync()
        {
            var result = await this.GetAllAsync(PartitionKeyNames.NotificationDataTable.DraftNotificationsPartition);

            return result;
        }

        /// <summary>
        /// Get the top 25 most recently sent notification entities from the table storage.
        /// </summary>
        /// <returns>The top 25 most recently sent notification entities.</returns>
        public async Task<IEnumerable<NotificationDataEntity>> GetMostRecentSentNotificationsAsync()
        {
            var result = await this.GetAllAsync(PartitionKeyNames.NotificationDataTable.SentNotificationsPartition, 25);

            return result;
        }

        /// <summary>
        /// Move a draft notification from draft to sent partition.
        /// </summary>
        /// <param name="draftNotificationEntity">The draft notification instance to be moved to the sent partition.</param>
        /// <returns>The new SentNotification ID.</returns>
        public async Task<string> MoveDraftToSentPartitionAsync(NotificationDataEntity draftNotificationEntity)
        {
            if (draftNotificationEntity == null)
            {
                throw new ArgumentNullException(nameof(draftNotificationEntity));
            }

            var newSentNotificationId = this.TableRowKeyGenerator.CreateNewKeyOrderingMostRecentToOldest();

            // Create a sent notification based on the draft notification.
            var sentNotificationEntity = new NotificationDataEntity
            {
                PartitionKey = PartitionKeyNames.NotificationDataTable.SentNotificationsPartition,
                RowKey = newSentNotificationId,
                Id = newSentNotificationId,
                Title = draftNotificationEntity.Title,
                ImageLink = draftNotificationEntity.ImageLink,
                Summary = draftNotificationEntity.Summary,
                Author = draftNotificationEntity.Author,
                ButtonTitle = draftNotificationEntity.ButtonTitle,
                ButtonLink = draftNotificationEntity.ButtonLink,
                CreatedBy = draftNotificationEntity.CreatedBy,
                CreatedDate = draftNotificationEntity.CreatedDate,
                SentDate = null,
                IsDraft = false,
                Teams = draftNotificationEntity.Teams,
                Rosters = draftNotificationEntity.Rosters,
                AllUsers = draftNotificationEntity.AllUsers,
                MessageVersion = draftNotificationEntity.MessageVersion,
                Succeeded = 0,
                Failed = 0,
                Throttled = 0,
                TotalMessageCount = draftNotificationEntity.TotalMessageCount,
                IsCompleted = false,
                SendingStartedDate = DateTime.UtcNow,
                IsPreparingToSend = true,
            };
            await this.CreateOrUpdateAsync(sentNotificationEntity);

            // Delete the draft notification.
            await this.DeleteAsync(draftNotificationEntity);

            return newSentNotificationId;
        }

        /// <summary>
        /// Duplicate an existing draft notification.
        /// </summary>
        /// <param name="notificationEntity">The notification entity to be duplicated.</param>
        /// <param name="createdBy">Created by.</param>
        /// <returns>A task that represents the work queued to execute.</returns>
        public async Task DuplicateDraftNotificationAsync(
            NotificationDataEntity notificationEntity,
            string createdBy)
        {
            var newId = this.TableRowKeyGenerator.CreateNewKeyOrderingOldestToMostRecent();

            var newNotificationEntity = new NotificationDataEntity
            {
                PartitionKey = PartitionKeyNames.NotificationDataTable.DraftNotificationsPartition,
                RowKey = newId,
                Id = newId,
                Title = notificationEntity.Title + " (copy)",
                ImageLink = notificationEntity.ImageLink,
                Summary = notificationEntity.Summary,
                Author = notificationEntity.Author,
                ButtonTitle = notificationEntity.ButtonTitle,
                ButtonLink = notificationEntity.ButtonLink,
                CreatedBy = createdBy,
                CreatedDate = DateTime.UtcNow,
                IsDraft = true,
                Teams = notificationEntity.Teams,
                Rosters = notificationEntity.Rosters,
                AllUsers = notificationEntity.AllUsers,
            };

            await this.CreateOrUpdateAsync(newNotificationEntity);
        }

        /// <summary>
        /// Save exception error message in a notification data entity.
        /// </summary>
        /// <param name="notificationDataEntityId">Notification data entity id.</param>
        /// <param name="errorMessage">Error message.</param>
        /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
        public async Task SaveExceptionInNotificationDataEntityAsync(
            string notificationDataEntityId,
            string errorMessage)
        {
            var notificationDataEntity = await this.GetAsync(
                PartitionKeyNames.NotificationDataTable.SentNotificationsPartition,
                notificationDataEntityId);
            if (notificationDataEntity != null)
            {
                notificationDataEntity.ExceptionMessage =
                    this.AppendNewLine(notificationDataEntity.ExceptionMessage, errorMessage);

                notificationDataEntity.IsCompleted = true;

                await this.CreateOrUpdateAsync(notificationDataEntity);
            }
        }

        /// <summary>
        /// Save warning message in a notification data entity.
        /// </summary>
        /// <param name="notificationDataEntityId">Notification data entity id.</param>
        /// <param name="warningMessage">Warning message to be saved.</param>
        /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
        public async Task SaveWarningInNotificationDataEntityAsync(
            string notificationDataEntityId,
            string warningMessage)
        {
            var notificationDataEntity = await this.GetAsync(
                PartitionKeyNames.NotificationDataTable.SentNotificationsPartition,
                notificationDataEntityId);
            if (notificationDataEntity != null)
            {
                notificationDataEntity.WarningMessage =
                    this.AppendNewLine(notificationDataEntity.WarningMessage, warningMessage);

                await this.CreateOrUpdateAsync(notificationDataEntity);
            }
        }

        private string AppendNewLine(string originalString, string newString)
        {
            return string.IsNullOrWhiteSpace(originalString)
                ? newString
                : $"{originalString}{Environment.NewLine}{newString}";
        }
    }
}
