# Teach online with EduCast

## Broadcast directly to streaming platforms

1. Open **Studio & clock…**, start a private camera preview, and select **Show on Program** when you want your camera in the lesson. Teach from the board or share an approved source.
2. To play a clip, choose **Media file…** in the Program panel or in the source window. EduCast opens supported local MP4, MOV, M4V, WebM, MP3, M4A, WAV, and AAC files. Select **Show on Program** to share the clip, then **Take preview** if you are in manual mode. The broadcast mixes the clip’s audio with the selected microphone. Returning to the board pauses the clip. Use **Resume source** and **Play** to continue it.
3. Choose **Live stream…**. Select the microphone, enable each destination, and paste the provider’s ingest server URL and stream key into separate fields. Use the RTMPS address when the provider supplies one. EduCast accepts plain RTMP for Twitch’s published ingest hosts and local testing.
4. Click **Go live** and watch the status next to each destination. Some providers require you to confirm the broadcast in their dashboard after receiving the preview. One failed destination leaves healthy destinations running. Click **Stop streams** in EduCast when you finish.
5. Check the broadcast on a second device before teaching. You can use a private or test event where the provider supports it. **Mute microphone** silences the mic. **Freeze** holds the picture and pauses the clip’s broadcast audio. **Blackout** sends a black picture and silent audio after any buffered stream data. Stop streaming before starting a local recording or applying an EduCast update.

The installer and portable ZIP include the media engine. EduCast sends 720p H.264/AAC at 25 frames per second directly to each enabled destination. Allow about 3–4 Mbps of upload capacity per destination. Stream keys are cleared from the form when a session starts and stay out of lesson files and logs. Keep the provider dashboard for account access, event scheduling, and provider-side confirmation.

- **YouTube** — Create an encoder stream in YouTube Studio and copy the RTMPS URL and key. [YouTube encoder setup](https://support.google.com/youtube/answer/2907883)
- **Facebook** — In Live Producer, choose streaming software and use the supplied server URL and key. [Facebook Live Producer](https://www.facebook.com/live/producer)
- **X** — In Media Studio Producer, create a source and copy its RTMPS URL and key. [X Media Studio Producer](https://help.x.com/en/using-x/how-to-use-live-producer)
- **Twitch** — Use the ingest URL and the stream key from your dashboard. [Twitch ingest setup](https://dev.twitch.tv/docs/video-broadcast/) · [Twitch simulcasting guidelines](https://help.twitch.tv/s/article/simulcasting-guidelines?language=en_US)

## Share the lesson in Zoom, Teams or Meet

In the EduCast Program panel, choose **Online window… → Open Online Program**. In the meeting app, share the individual **EduCast · Online Program** window and choose your microphone in that app. EduCast can keep the projector on while the meeting sees the same approved picture. Online Program is a window share, and its media audio is carried by EduCast’s direct broadcasts. [Zoom window sharing](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0060607)
