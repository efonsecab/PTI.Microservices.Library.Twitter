using LinqToTwitter;
using Microsoft.Extensions.Logging;
using PTI.Microservices.Library.Configuration;
using PTI.Microservices.Library.Interceptors;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PTI.Microservices.Library.Services
{
    /// <summary>
    /// Service in cahrge of eposing access to Twitter functionality
    /// </summary>
    public sealed class TwitterService
    {
        private ILogger<TwitterService> Logger { get; }
        private TwitterConfiguration TwitterConfiguration { get; }
        private CustomHttpClient CustomHttpClient { get; }

        /// <summary>
        /// Twitter connection context
        /// </summary>
        private TwitterContext TwitterContext { get; }

        /// <summary>
        /// Creates a new instance of <see cref="TwitterService"/>
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="twitterConfiguration"></param>
        /// <param name="customHttpClient"></param>
        public TwitterService(ILogger<TwitterService> logger, TwitterConfiguration twitterConfiguration,
            CustomHttpClient customHttpClient)
        {
            this.Logger = logger;
            this.TwitterConfiguration = twitterConfiguration;
            this.CustomHttpClient = customHttpClient;
            LinqToTwitter.TwitterContext twitterContext =
                new LinqToTwitter.TwitterContext(
                    new LinqToTwitter.SingleUserAuthorizer()
                    {
                        CredentialStore = new LinqToTwitter.SingleUserInMemoryCredentialStore()
                        {
                            AccessToken = this.TwitterConfiguration.AccessToken,
                            AccessTokenSecret = this.TwitterConfiguration.AccessTokenSecret,
                            ConsumerKey = this.TwitterConfiguration.ConsumerKey,
                            ConsumerSecret = this.TwitterConfiguration.ConsumerSecret,
                            ScreenName = this.TwitterConfiguration.ScreenName
                        }
                    });
            this.TwitterContext = twitterContext;
        }

        private bool isTwitterUserVerified { get; set; } = false;
        /// <summary>
        /// Verifies the user credentials
        /// </summary>
        /// <returns></returns>
        public async Task<bool> VerifyMyUserAsync()
        {
            try
            {
                var verifyResponse =
                    await
                        (from acct in this.TwitterContext.Account
                         where acct.Type == AccountType.VerifyCredentials
                         select acct)
                        .SingleOrDefaultAsync();

                if (verifyResponse != null && verifyResponse.User != null)
                {
                    User user = verifyResponse.User;
                    this.isTwitterUserVerified = user.ScreenNameResponse == this.TwitterConfiguration.ScreenName;
                    return this.isTwitterUserVerified;
                }
            }
            catch (TwitterQueryException tqe)
            {
                this.Logger?.LogError(tqe, tqe.Message);
                throw;
            }
            return false;
        }

        /// <summary>
        /// Searches for tweets with the given term
        /// </summary>
        /// <param name="searchTerm"></param>
        /// <returns></returns>
        public async Task<List<Status>> SearchTweetsAsync(string searchTerm)
        {
            List<Status> result = null;
            try
            {
                Search searchResponse =
                    await
                    (from search in this.TwitterContext.Search
                     where search.Type == SearchType.Search &&
                           search.Query == searchTerm &&
                           search.IncludeEntities == true &&
                           search.TweetMode == TweetMode.Extended
                     select search)
                    .SingleOrDefaultAsync();
                result = searchResponse.Statuses;
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
                throw;
            }
            return result;
        }

        /// <summary>
        /// Gets tweets by te given id
        /// </summary>
        /// <param name="tweetId"></param>
        /// <returns></returns>
        public async Task<Status> GetTweetByStatusIdAsync(ulong tweetId)
        {
            Status result = null;
            bool mustStop = false;
            int currentRetries = 0;
            do
            {
                try
                {
                    result = await this.TwitterContext.Status.Where(p => p.ID == tweetId &&
                    p.Type == StatusType.Show &&
                    p.TweetMode == TweetMode.Extended &&
                    p.IncludeEntities == true &&
                    p.IncludeAltText == true
                    ).SingleOrDefaultAsync();
                    mustStop = true;
                }
                catch (Exception ex)
                {
                    this.Logger?.LogError(ex, ex.Message);
                    if (this.TwitterConfiguration.RetryOperationOnFailure && currentRetries < this.TwitterConfiguration.MaxRetryCount)
                    {
                        currentRetries++;
                        await EvaluateIfRateLimitExceededAsync();
                    }
                    else
                        mustStop = true;
                }
            }
            while (!mustStop);
            return result;
        }

        /// <summary>
        /// Retrieves a list of the latest tweets for the specified user id
        /// </summary>
        /// <param name="userId">Id of the user to retrieve tweets from</param>
        /// <param name="maxTweets">Maximum number of items to retrieve. Default is 10</param>
        /// <param name="sinceTweetId">Tweets it to start from</param>
        /// <returns></returns>
        public async Task<List<Status>> GetTweetsByUserIdAsync(ulong userId, int? maxTweets = 10, ulong? sinceTweetId = 1)
        {
            List<Status> lstTweets = null;
            try
            {
                lstTweets = await this.TwitterContext.Status.Where(p =>
                p.Type == StatusType.User &&
                p.UserID == userId &&
                p.Count == maxTweets &&
                p.SinceID == sinceTweetId
                ).ToListAsync();
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
            }
            return lstTweets;
        }

        /// <summary>
        /// Gets the complete text for a tweet
        /// </summary>
        /// <param name="tweet"></param>
        /// <returns></returns>
        public string GetCompleteTweetText(LinqToTwitter.Status tweet)
        {
            string result = tweet.FullText;
            if (string.IsNullOrWhiteSpace(result))
                if (tweet.Retweeted)
                {
                    if (tweet.RetweetedStatus != null && tweet.RetweetedStatus.ExtendedTweet != null)
                        result = tweet.RetweetedStatus.ExtendedTweet.FullText ?? tweet.RetweetedStatus.ExtendedTweet.Text;
                    else
                        result = tweet.RetweetedStatus.FullText ?? tweet.RetweetedStatus.Text;
                }
            if (string.IsNullOrWhiteSpace(result))
                result = tweet.Text;

            return result;
        }

        /// <summary>
        /// Retrieves a list of the latest tweets for the specified user id
        /// </summary>
        /// <param name="username">username to retrieve tweets from</param>
        /// <param name="maxTweets">Maximum number of items to retrieve. Default is 10</param>
        /// <param name="sinceTweetId">Tweets it to start from</param>
        /// <param name="includeRetweets"></param>
        /// <param name="excludeReplies"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<Status>> GetTweetsByUsernameAsync(string username, int maxTweets = 10, ulong sinceTweetId = 1,
            bool includeRetweets = true, bool excludeReplies = true, CancellationToken cancellationToken = default)
        {
            List<Status> lstTweets = null;
            bool mustStop = false;
            int currentRetries = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    this.Logger?.LogInformation($"Scanning message for: {username}. Current Retries: {currentRetries}");
                    var query = this.TwitterContext.Status.Where(p =>
                    p.Type == StatusType.User &&
                    p.ScreenName == username &&
                    p.Count == maxTweets &&
                    p.SinceID == sinceTweetId &&
                    //p.Retweeted == isRetweeted &&
                    p.ExcludeReplies == excludeReplies &&
                    p.IncludeEntities == true &&
                    p.IncludeRetweets == includeRetweets &&
                    p.TweetMode == TweetMode.Extended
                    );
                    lstTweets = await query.ToListAsync();
                    mustStop = true;

                }
                catch (Exception ex)
                {
                    this.Logger?.LogError(ex, ex.Message);
                    if (this.TwitterConfiguration.RetryOperationOnFailure && currentRetries < this.TwitterConfiguration.MaxRetryCount)
                    {
                        currentRetries++;
                        await EvaluateIfRateLimitExceededAsync();
                    }
                    else
                        mustStop = true;
                }
            }
            while (!mustStop);
            return lstTweets;
        }

        /// <summary>
        /// Retrieves the information for a specified username
        /// </summary>
        /// <param name="username"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<User> GetUserInfoByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            User userInfo = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                userInfo = await this.TwitterContext.User.Where(p =>
                p.Type == UserType.Show &&
                p.ScreenName == username
                ).SingleOrDefaultAsync();
                return userInfo;
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
            }
            return userInfo;
        }

        /// <summary>
        /// Gets a list of users not following back the specified user
        /// </summary>
        /// <param name="username"></param>
        /// <param name="maxUsers"></param>
        /// <param name="cursor"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List<User>> GetUsersNotFollowingBackByUsernameAsnc(string username, int maxUsers = 10, long? cursor = -1,
            CancellationToken cancellationToken = default)
        {
            List<User> lstUsersNotFollowingBack = new List<User>();
            bool mustStop = false;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var friendship =
                    await
                    (from friend in this.TwitterContext.Friendship
                     where friend.Type == FriendshipType.FriendsList &&
                           friend.ScreenName == username &&
                           friend.Cursor == cursor
                     select friend)
                    .SingleOrDefaultAsync();
                if (friendship != null &&
                    friendship.Users != null &&
                    friendship.CursorMovement != null)
                {
                    foreach (var singleUser in friendship.Users)
                    {
                        bool isFollowingBack = await IsFollowingBackAsync(mainAccount: username, followerValidating: singleUser.ScreenNameResponse);
                        lstUsersNotFollowingBack.Add(singleUser);
                        await this.EvaluateIfRateLimitExceededAsync();
                    }
                    if (friendship != null &&
                    friendship.Users != null &&
                    friendship.CursorMovement != null)
                    {
                        cursor = friendship.CursorMovement.Next;
                    }
                }
                mustStop = true;
            } while (!mustStop);
            return lstUsersNotFollowingBack;

        }

        /// <summary>
        /// Detects if the a user is following back the mainAccount user
        /// </summary>
        /// <param name="mainAccount"></param>
        /// <param name="followerValidating"></param>
        /// <returns></returns>
        public async Task<bool> IsFollowingBackAsync(string mainAccount, string followerValidating)
        {
            bool isFollowingBack = false;
            var friendships = await (from friendship in this.TwitterContext.Friendship
                                     where friendship.Type == FriendshipType.Show &&
                                     friendship.SourceScreenName == mainAccount &&
                                     friendship.TargetScreenName == followerValidating
                                     select friendship).SingleOrDefaultAsync();
            if (friendships != null)
            {
                isFollowingBack = (friendships.SourceRelationship.FollowedBy);
            }
            return isFollowingBack;
        }

        /// <summary>
        /// Retrieves the followers for a specified user
        /// </summary>
        /// <param name="username"></param>
        /// <param name="maxFollowers"></param>
        /// <param name="cursor"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Friendship> GetUserFollowersByUsernameAsync(string username, int maxFollowers = 10,
            long? cursor = null, CancellationToken cancellationToken = default)
        {
            Friendship followers = null;
            bool mustStop = false;
            int currentRetries = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    this.Logger?.LogInformation($"Scanning followers for: {username}. Cursor: {cursor}. Current Retries: {currentRetries}");
                    await this.EvaluateIfRateLimitExceededAsync();
                    var followersQuery = this.TwitterContext.Friendship.Where(p =>
                    p.Type == FriendshipType.FollowersList &&
                    p.ScreenName == username &&
                    p.Count == maxFollowers
                    );
                    if (cursor != null)
                        followersQuery = followersQuery.Where(p => p.Cursor == cursor);
                    followers = await followersQuery.SingleOrDefaultAsync();
                    mustStop = true;
                }
                catch (Exception ex)
                {
                    this.Logger?.LogError(ex, ex.Message);
                    if (this.TwitterConfiguration.RetryOperationOnFailure && currentRetries < this.TwitterConfiguration.MaxRetryCount)
                    {
                        currentRetries++;
                        await EvaluateIfRateLimitExceededAsync();
                    }
                    else
                        mustStop = true;
                }
            } while (!mustStop);
            return followers;
        }


        /// <summary>
        /// Blocks the specified user
        /// </summary>
        /// <param name="username"></param>
        /// <returns></returns>
        public async Task<User> BlockUserByUsernameAsync(string username)
        {
            try
            {
                //check https://github.com/JoeMayo/LinqToTwitter/wiki/Reporting-Spam
                var result = await this.TwitterContext.CreateBlockAsync(0, username, true);
                return result;
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Unblocks the specified user
        /// </summary>
        /// <param name="username"></param>
        /// <returns></returns>
        public async Task<User> UnBlockUserByUsernameAsync(string username)
        {
            try
            {
                //check https://github.com/JoeMayo/LinqToTwitter/wiki/Reporting-Spam
                var result = await this.TwitterContext.DestroyBlockAsync(0, username, true);
                return result;
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// List mode
        /// </summary>
        public enum ListMode
        {
            /// <summary>
            /// Private
            /// </summary>
            Private = 0,
            /// <summary>
            /// Public
            /// </summary>
            Public = 1
        }

        /// <summary>
        /// Creates a new list
        /// </summary>
        /// <param name="listName"></param>
        /// <param name="mode"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<List> CreateListAsync(string listName, ListMode mode, CancellationToken cancellationToken = default)
        {
            try
            {
                var existentList = await this.SearchOwnedListByNameAsync(listName);
                if (existentList != null)
                    throw new Exception("There is already a list with the same name");
                var result = await
                this.TwitterContext.CreateListAsync(listName, mode.ToString().ToLower(), null, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Adds users to a specified list
        /// </summary>
        /// <param name="usernames"></param>
        /// <param name="listId"></param>
        /// <returns></returns>
        public async Task<List> AddUsersToListAsync(List<string> usernames, ulong listId)
        {
            try
            {
                //Check https://github.com/JoeMayo/LinqToTwitter/wiki/Adding-Multiple-Members-to-a-List
                if (usernames.Count > 100)
                    throw new Exception("List of usernames must not exceed 100 items");
                var result =
                    await this.TwitterContext.AddMemberRangeToListAsync(
                        listId, null, 0, this.TwitterConfiguration.ScreenName, usernames);
                return result;
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Finds the owned list with the given name
        /// </summary>
        /// <param name="listName"></param>
        /// <returns></returns>
        public async Task<List> SearchOwnedListByNameAsync(string listName)
        {
            try
            {
                var result = await this.TwitterContext.List.Where(p =>
                p.Name == listName && p.Type == ListType.Ownerships && p.ScreenName == this.TwitterConfiguration.ScreenName).SingleOrDefaultAsync();
                return result;
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
                throw;
            }
        }
        /// <summary>
        /// Evaluates if Rate Limit has been excedded and waits the specified time by twitter api
        /// </summary>
        private async Task EvaluateIfRateLimitExceededAsync()
        {
            if (this.TwitterContext.RateLimitRemaining == 0)
            {
                var d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                d = d.AddSeconds(this.TwitterContext.RateLimitReset);
                var timeToWait = d.Subtract(DateTime.UtcNow);
                int iMillisecondsToWait = (int)Math.Ceiling(timeToWait.TotalMilliseconds);
                if (iMillisecondsToWait < -1)
                {
                    iMillisecondsToWait = 30 * 1000; //30 seconds
                }
                var totalMinutes = TimeSpan.FromMilliseconds(iMillisecondsToWait).TotalMinutes;
                var resumeTime = DateTime.Now.AddMinutes(totalMinutes);
                this.Logger?.LogInformation($"Reached Twitters APIs Limits. Waiting for: {totalMinutes} minutes. " +
                    $"Resuming at local time: {resumeTime}");
                await Task.Delay(iMillisecondsToWait);
            }
        }

        /// <summary>
        /// Sends a direct message to the specified user
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="message"></param>
        /// <param name="throwOnError"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task SendDirectMessageAsync(ulong userId, string message, bool throwOnError = false, CancellationToken cancellationToken = default)
        {
            try
            {
                //Check https://github.com/JoeMayo/LinqToTwitter/wiki/Sending-Direct-Message-Events
                var result = await this.TwitterContext.NewDirectMessageEventAsync(userId, message, cancellationToken);
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex, ex.Message);
                if (throwOnError)
                    throw;
            }
            finally
            {
                if (!throwOnError)
                    await EvaluateIfRateLimitExceededAsync();
            }
        }

        /// <summary>
        /// Media type for uploading media
        /// </summary>
        public enum MediaType
        {
            /// <summary>
            /// image/jpg
            /// </summary>
            JPG,
            /// <summary>
            /// image/png
            /// </summary>
            PNG,
            /// <summary>
            /// video/mp4
            /// </summary>
            MP4
        }

        /// <summary>
        /// Media category for uploading media
        /// Check https://twittercommunity.com/t/media-category-values/64781/6
        /// </summary>
        public enum MediaCategory
        {
            /// <summary>
            /// Image
            /// </summary>
            tweet_image,
            /// <summary>
            /// GIF
            /// </summary>
            tweet_gif,
            /// <summary>
            /// Video
            /// </summary>
            tweet_video,
            /// <summary>
            /// Amplify video
            /// </summary>
            amplify_video
        }

        /// <summary>
        /// Check https://github.com/JoeMayo/LinqToTwitter/wiki/Tweeting-with-Media
        /// </summary>
        /// <param name="message"></param>
        /// <param name="imageAlternateText"></param>
        /// <param name="imageBytes"></param>
        /// <param name="mediaType"></param>
        /// <param name="mediaCategory"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<Status> TweetWithMediaAsync(string message, string imageAlternateText,
            byte[] imageBytes, MediaType mediaType, MediaCategory mediaCategory,
            CancellationToken cancellationToken = default)
        {
            try
            {
                string strMediaType = string.Empty;
                switch (mediaType)
                {
                    case MediaType.PNG:
                        strMediaType = "image/png";
                        break;
                    case MediaType.JPG:
                        strMediaType = "image/png";
                        break;
                    case MediaType.MP4:
                        strMediaType = "image/mp4";
                        break;
                }

                var uploadedMedia = await this.TwitterContext.UploadMediaAsync(imageBytes, strMediaType,
                    mediaCategory.ToString(), cancelToken: cancellationToken);
                return await this.TwitterContext.TweetAsync(message,
                    mediaIds: new ulong[] { uploadedMedia.MediaID });
            }
            catch (Exception ex)
            {
                this.Logger?.LogError(ex.Message, ex);
                throw;
            }
        }
    }
}
