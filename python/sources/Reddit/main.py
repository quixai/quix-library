import quixstreaming
import os
import praw
import json
import threading
import datetime

shutting_down = False
counter = 0
def main():
    global counter
    print("Configuring Reddit client")
    reddit = praw.Reddit(
        user_agent="QuixRedditScraper (by u/" + os.environ['reddit_username'] + ")",
        client_id=os.environ['reddit_client_id'],
        client_secret=os.environ['reddit_client_secret'],
        username=os.environ['reddit_username'],
        password=os.environ['reddit_password']
    )
    subreddit_name = os.environ['subreddit']
    print("Preparing to read subreddit " + subreddit_name)
    subreddit = reddit.subreddit(subreddit_name)

    print("Preparing Quix stream")
    client = quixstreaming.QuixStreamingClient()
    output_topic = client.open_output_topic(os.environ["output"])
    subreddit_stream = output_topic.create_stream(subreddit_name)
    subreddit_stream.parameters.buffer.time_span_in_milliseconds = 100

    print("Getting subreddit stream")
    for submission in subreddit.stream.submissions():
        if shutting_down:
            return
        sub = {
            "id": submission.id,
            "spoiler": submission.spoiler,
            "over_18": submission.over_18,
            "title": submission.title,
            "award_count": submission.total_awards_received,
            "awarder_count": len(submission.awarders),
            "created_utc": int(submission.created_utc),
            "thumbnail": submission.thumbnail,
            "thumbnail_height": submission.thumbnail_height,
            "thumbnail_width": submission.thumbnail_width,
            "url": submission.url,
            "score": submission.score,
            "upvote_ratio": submission.upvote_ratio,
            "shortlink": submission.shortlink
        }
        if hasattr(submission, 'post_hint'):
            sub["post_hint"] = submission.post_hint
        subreddit_stream.events.add_timestamp(datetime.datetime.utcnow()).add_value("submission", json.dumps(sub)).write()
        pdata = subreddit_stream.parameters.buffer.add_timestamp(datetime.datetime.utcnow())
        pdata.add_value("spoiler", 1 if sub["spoiler"] else 0) \
             .add_value("over_18", 1 if sub["over_18"] else 0) \
             .add_value("title", sub["title"]) \
             .add_value("award_count", sub["award_count"]) \
             .add_value("awarder_count", sub["awarder_count"]) \
             .add_value("created_utc", sub["created_utc"]) \
             .add_value("thumbnail", sub["thumbnail"]) \
             .add_value("thumbnail_height", sub["thumbnail_height"]) \
             .add_value("thumbnail_width", sub["thumbnail_width"]) \
             .add_value("url", sub["url"]) \
             .add_value("score", sub["score"]) \
             .add_value("upvote_ratio", sub["upvote_ratio"]) \
             .add_value("shortlink", sub["shortlink"])
        if "post_hint" in sub:
            pdata.add_value("shortlink", sub["shortlink"])
        pdata.add_tag("id", sub["id"])
        pdata.write()
        counter = counter + 1
        print("Sent submission count: " + str(counter))
    print("Subreddit stream finished")

def before_shutdown():
    global shutting_down
    print("Shutting down")
    shutting_down = True

if __name__ == "__main__":
    main_thread = threading.Thread(target = main)
    main_thread.start()
    quixstreaming.App.run(before_shutdown=before_shutdown)
    print ("Finished shutdown")