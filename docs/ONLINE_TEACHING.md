# Teach online with EduCast

In EduCast’s Program panel, select **Zoom & streaming… → Open Online Program**. This opens **EduCast · Online Program**, a separate 16:9 view of the same approved content the projector sees. Leave the window open and restored while sharing. You can work in the teacher window and switch between **Board** and **Resume source** while the online audience stays on the same feed.

## Share in Zoom, Teams or Meet

1. Join the meeting and select your microphone in the meeting app.
2. Open the app’s screen-share picker. In Zoom Workplace, choose **Share → Screens** and select **EduCast · Online Program** from the individual windows.
3. Check the shared view from another device or ask a participant to confirm they see the board. Use **Freeze** to hold the approved image or **Blackout** to blank it. Stop sharing in the meeting app when you finish.

[Zoom window sharing](https://support.zoom.com/hc/en/article?id=zm_kb&sysparm_article=KB0060607)

## Use EduCast as a camera

1. In OBS Studio, add a **Window Capture** source and select **EduCast · Online Program**. Set **Window Match Priority** to require the exact title and turn off **Capture Cursor**. Fit the source to the OBS canvas and check the preview before connecting it to a meeting.
2. In OBS, set the virtual camera to **Program** and click **Start Virtual Camera**.
3. In Zoom, Teams or Meet, choose **OBS Virtual Camera** as the camera and select your microphone separately. The virtual camera carries video. For a board with small writing, Zoom window sharing usually gives students more room to read.

[OBS Window Capture](https://obsproject.com/kb/window-capture-sources) · [OBS Virtual Camera](https://obsproject.com/kb/virtual-camera-guide)

## Broadcast to YouTube, Facebook, X and Twitch at the same time

1. Use the same OBS window-capture scene. Select the teaching microphone in OBS and verify its meter. If you are broadcasting a class that meets on Zoom, confirm whether you are sending only your narration or the meeting audio too.
2. In Restream, connect the eligible YouTube, Facebook, X and Twitch accounts. Create or open the encoder stream and enable the destinations for this session. Check platform eligibility and your Restream plan’s channel limits before scheduling a class.
3. In OBS, open **Settings → Stream**, choose **Restream.io**, then connect your account or use the encoder stream key from Restream. Start with the encoder settings shown in Restream. Keep stream keys in the encoder.
4. Rehearse with a private destination where supported. Check audio and verify that EduCast’s teacher notes and private previews are absent from the feed. Before a public broadcast, confirm you have permission to share any student voices or images. Start streaming from OBS and verify that each enabled destination is live. Stop streaming in OBS when class ends.

OBS supplies the virtual camera and encoding. Restream distributes the encoded stream to the selected destinations. EduCast supplies the approved Program picture. The captured teaching source is video only, and the microphone configured for EduCast’s local MP4 recordings is separate from the microphone configured in OBS or Zoom.

[Connect OBS to Restream](https://support.restream.io/en/articles/111656-connect-obs-to-restream) · [Manage Restream channels](https://support.restream.io/en/articles/10053865-how-to-add-and-enable-your-channels) · [Supported destinations](https://support.restream.io/en/articles/872029-supported-social-platforms) · [Twitch simulcasting rules](https://help.twitch.tv/s/article/simulcasting-guidelines?language=en_US)
