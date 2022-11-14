# PTI.Microservices.Library.Twitter

This is part of PTI.Microservices.Library set of packages

The purpose of this package is to facilitate cosuming the Twitter APIs

**Examples:**

## Get Tweets By Username
    TwitterService twitterService =
                    new TwitterService(null, this.TwitterConfiguration,
                    new Microservices.Library.Interceptors.CustomHttpClient(
                        new Microservices.Library.Interceptors.CustomHttpClientHandler(null)));
    var result = await twitterService.GetTweetsByUsernameAsync("dotnet");

## Tweet With Media
    TwitterService twitterService =
        new TwitterService(null, this.TwitterConfiguration,
        new Microservices.Library.Interceptors.CustomHttpClient(
            new Microservices.Library.Interceptors.CustomHttpClientHandler(null)));
    StringBuilder stringBuilder = new StringBuilder();
    stringBuilder.AppendLine( $"Testing tweeting with PTI.Microservices.Library: https://github.com/efonsecab/PTI.Microservices.Library");
    stringBuilder.AppendLine("@pticostarica Microservices library");
    stringBuilder.AppendLine("#dotnet #dotnetcore");
    var result = await twitterService.TweetWithMediaAsync(stringBuilder.ToString(),
        "PTI.Microservices.Library tweting test",
        imageBytes,TwitterService.MediaType.PNG, TwitterService.MediaCategory.tweet_image);

## Get Users Not Following Back By Username
    TwitterService twitterService =
        new TwitterService(null, this.TwitterConfiguration,
        new Microservices.Library.Interceptors.CustomHttpClient(new Microservices.Library.Interceptors.CustomHttpClientHandler(null)));
    var result = await twitterService.GetUsersNotFollowingBackByUsernameAsnc([YOURUSERNAME]);

## Search Tweets
    TwitterService twitterService =
        new TwitterService(null, this.TwitterConfiguration,
        new Microservices.Library.Interceptors.CustomHttpClient(new Microservices.Library.Interceptors.CustomHttpClientHandler(null)));
    var results = await twitterService.SearchTweetsAsync("#dotnet");