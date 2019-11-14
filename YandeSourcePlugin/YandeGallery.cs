﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Wbooru;
using Wbooru.Galleries;
using Wbooru.Galleries.SupportFeatures;
using Wbooru.Models;
using Wbooru.Models.Gallery;
using Wbooru.Network;
using Wbooru.PluginExt;
using Wbooru.Settings;
using Wbooru.Utils;

namespace YandeSourcePlugin
{
    [Export(typeof(Gallery))]
    public class YandeGallery : Gallery, IGalleryTagSearch, IGallerySearchImage , IGalleryItemIteratorFastSkipable
    {
        public override string GalleryName => "Yande";

        public GlobalSetting setting;

        HashSet<string> c=new HashSet<string>();

        public YandeGallery()
        {
            setting = SettingManager.LoadSetting<GlobalSetting>();
        }

        public override GalleryImageDetail GetImageDetial(GalleryItem item)
        {
            if (!((item as PictureItem)?.GalleryDetail is GalleryImageDetail detail))
            {
                if (item.GalleryName != GalleryName)
                    throw new Exception($"This item doesn't belong with gallery {GalleryName}.");

                detail = (GetImage(item.GalleryItemID) as PictureItem).GalleryDetail;
            }

            return detail;
        }

        public IEnumerable<GalleryItem> GetImagesInternal(IEnumerable<string> tags=null,int page = 1)
        {
            var limit = SettingManager.LoadSetting<YandeSetting>().PicturesCountPerRequest;
            limit = limit == 0 ? SettingManager.LoadSetting<GlobalSetting>().GetPictureCountPerLoad : limit;

            var base_url = $"https://yande.re/post.json?limit={limit}&";

            if (tags?.Any()??false)
                base_url += $"tags={string.Join("+",tags)}&";

            while (true)
            {
                JArray json=null;

                try
                {
                    var actual_url = $"{base_url}page={page}";

                    var response = RequestHelper.CreateDeafult(actual_url);
                    using var reader = new StreamReader(response.GetResponseStream());

                    json = JsonConvert.DeserializeObject(reader.ReadLine()) as JArray;

                    if (json.Count == 0)
                        break;
                }
                catch (Exception e)
                {
                    ExceptionHelper.DebugThrow(e);
                }

                foreach (var pic_info in json)
                {
                    var item = BuildItem(pic_info);

                    c.Add(item.GalleryItemID);

                    yield return item;
                }

                page++;
            }

            Log<YandeGallery>.Info("there is no pic that gallery could provide.");
        }

        private GalleryItem BuildItem(JToken pic_info)
        {
            PictureItem item = new PictureItem();

            item.GalleryItemID = pic_info["id"].ToString();
            item.PreviewImageDownloadLink = pic_info["preview_url"].ToString();
            item.PreviewImageSize = new Size(pic_info["preview_width"].ToObject<int>(), pic_info["preview_height"].ToObject<int>());

            var detail = new GalleryImageDetail();

            detail.ID = item.GalleryItemID;
            detail.Rate = pic_info["rating"].ToString();
            detail.Tags = pic_info["tags"].ToString().Split(' ').ToList();
            detail.Updater = pic_info["creator_id"].ToString();
            detail.CreateDate = DateTimeOffset.FromUnixTimeSeconds(pic_info["created_at"].ToObject<long>()).DateTime;
            detail.Author = pic_info["author"].ToString();
            detail.Resolution = new Size(pic_info["width"].ToObject<int>(), pic_info["height"].ToObject<int>());
            detail.Score = pic_info["score"].ToString();

            List<DownloadableImageLink> downloads = new List<DownloadableImageLink>();

            downloads.Add(new DownloadableImageLink()
            {
                Description = "Jpeg",
                Size = new Size(pic_info["jpeg_width"].ToObject<int>(), pic_info["jpeg_height"].ToObject<int>()),
                FileLength = pic_info["jpeg_file_size"].ToObject<int>(),
                DownloadLink = pic_info["jpeg_url"].ToString(),
                FullFileName = WebUtility.UrlDecode(Path.GetFileName(pic_info["jpeg_url"].ToString()))
            });

            downloads.Add(new DownloadableImageLink()
            {
                Description = "Preview",
                Size = new Size(pic_info["preview_width"].ToObject<int>(), pic_info["preview_height"].ToObject<int>()),
                FileLength = 0,
                DownloadLink = pic_info["preview_url"].ToString(),
                FullFileName = WebUtility.UrlDecode(Path.GetFileName(pic_info["preview_url"].ToString()))
            });

            downloads.Add(new DownloadableImageLink()
            {
                Description = "Sample",
                Size = new Size(pic_info["sample_width"].ToObject<int>(), pic_info["sample_height"].ToObject<int>()),
                FileLength = pic_info["sample_file_size"].ToObject<int>(),
                DownloadLink = pic_info["sample_url"].ToString(),
                FullFileName = WebUtility.UrlDecode(Path.GetFileName(pic_info["sample_url"].ToString()))
            });

            downloads.Add(new DownloadableImageLink()
            {
                Description = "File",
                Size = new Size(pic_info["width"].ToObject<int>(), pic_info["height"].ToObject<int>()),
                FileLength = pic_info["file_size"].ToObject<int>(),
                DownloadLink = pic_info["file_url"].ToString(),
                FullFileName = WebUtility.UrlDecode(Path.GetFileName(pic_info["file_url"].ToString()))
            });

            detail.DownloadableImageLinks = downloads;

            item.GalleryDetail = detail;

            item.GalleryName = GalleryName;

            item.DownloadFileName = $"{item.GalleryItemID} {string.Join(" ", detail.Tags)}";

            return item;
        }

        public IEnumerable<GalleryItem> SearchImages(IEnumerable<string> keywords)
            => GetImagesInternal(keywords);

        public override IEnumerable<GalleryItem> GetMainPostedImages() => GetImagesInternal();

        public IEnumerable<Tag> SearchTag(string keywords)
        {
            var response = RequestHelper.CreateDeafult($"https://yande.re/tag.json?order=name&limit=0&name={keywords}");
            using var reader = new StreamReader(response.GetResponseStream());

            var arr = JsonConvert.DeserializeObject(reader.ReadLine()) as JArray;

            foreach (var item in arr)
            {
                yield return new Tag()
                {
                    Name = item["name"].ToString(),
                    Type = item["type"].ToString() switch
                    {
                        "0" => TagType.General,
                        "1" => TagType.Artist,
                        //"2" => TagType.Character,
                        "3" => TagType.Copyright,
                        "4" => TagType.Character,
                        "5" => TagType.Circle,
                        "6" => TagType.Faults,
                        _ => TagType.Unknown
                    }
                };
            }
        }

        public override GalleryItem GetImage(string id)
        {
            try
            {
                var response = RequestHelper.CreateDeafult($"https://yande.re/post/show/{id}");

                using var reader = new StreamReader(response.GetResponseStream());
                var content = reader.ReadToEnd();

                const string CONTENT_HEAD = "Post.register_resp(";
                var start_index = content.LastIndexOf(CONTENT_HEAD) + CONTENT_HEAD.Length;
                StringBuilder builder = new StringBuilder(1024);
                int stack = 1;

                foreach (var ch in content.Skip(start_index))
                {
                    if (ch == ')')
                    {
                        stack--;
                        if (stack == 0)
                            break;
                    }

                    if (ch == '(')
                        stack++;

                    builder.Append(ch);
                }

                var result = JsonConvert.DeserializeObject(builder.ToString()) as JObject;
                return BuildItem((result["posts"] as JArray).FirstOrDefault());
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                ExceptionHelper.DebugThrow(e);
                return null;
            }
        }

        public IEnumerable<GalleryItem> IteratorSkip(int skip_count)
        {
            var limit_count = SettingManager.LoadSetting<GlobalSetting>().GetPictureCountPerLoad;

            var page = skip_count / limit_count + 1;
            skip_count = skip_count % SettingManager.LoadSetting<GlobalSetting>().GetPictureCountPerLoad;

            return GetImagesInternal(null, page).Skip(skip_count);
        }
    }
}
