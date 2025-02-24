using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Tesseract;

namespace AdeptiScanner_ZZZ
{
    class ImageProcessing
    {
        public static List<Point> getArtifactGrid(Bitmap areaImg, bool saveImages, Point coordOffset)
        {
            //Rectangle area = new Rectangle(gameArea.X, gameArea.Y, artifactArea.X - gameArea.X, gameArea.Height);
            //Get relevant part of image
            //Bitmap areaImg = new Bitmap(area.Width, area.Height);
            //using (Graphics g = Graphics.FromImage(areaImg))
            //{
            //    g.DrawImage(img, 0, 0, area, GraphicsUnit.Pixel);
            //}
            //Prepare bytewise image processing
            int width = areaImg.Width;
            int height = areaImg.Height;
            BitmapData imgData = areaImg.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            int PixelSize = 4; //ARGB, reverse order

            if (!saveImages)
            {
                areaImg.UnlockBits(imgData);
            }


            List<(int StartX, int EndX, int Y)> artifactListTuple = new(); //start and end x, y for found artifacts in grid
            int currStreak = 0;
            int margin = 4;
            GameColor? targetColor = null;

            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % width;
                int y = (i / PixelSize - x) / width;
                int y_low = Math.Min(y + 10, height - 1);
                int i_low = (y_low * width + x) * PixelSize;
                var pixel = imgBytes.AsSpan(i, 4);
                var pixelBelow = imgBytes.AsSpan(i_low, 4);

                if (!targetColor.HasValue)
                {
                    if (PixelIsColor(pixel, GameColor.ArtifactLabelB))
                    {
                        targetColor = GameColor.ArtifactLabelB;
                    } 
                    else if (PixelIsColor(pixel, GameColor.ArtifactLabelA))
                    {
                        targetColor = GameColor.ArtifactLabelA;
                    }
                    else if(PixelIsColor(pixel, GameColor.ArtifactLabelS))
                    {
                        targetColor = GameColor.ArtifactLabelS;
                    } else
                    {
                        continue;
                    }
                }

                if (PixelIsColor(pixel, targetColor.Value))
                {
                    if (saveImages)
                    {
                    imgBytes[i] = 255;
                    imgBytes[i + 1] = 0;
                    imgBytes[i + 2] = 255;
                    imgBytes[i + 3] = 255;
                    }
                    currStreak++;
                    margin = 3;
                }
                else
                {
                    //give some margin of error before skipping
                    if (margin > 0)
                    {
                        margin--;
                        continue;
                    }

                    //investigate label if labelstreak long enough
                    if (currStreak > 5)
                    {
                        //find left edge
                        int left = Math.Max(x - currStreak - 3, 1);
                        margin = 3;
                        for (int left_i = (y_low * width + left) * PixelSize; left_i > y_low * width * PixelSize; left_i-= PixelSize)
                        {
                            var leftPixel = imgBytes.AsSpan(left_i, 4);
                            if (PixelIsColor(leftPixel, targetColor.Value))
                            {
                                margin = 3;
                            } else
                            {
                                margin--;
                            }

                            if( margin > 0)
                            {
                                left--;
                            } else
                            {
                                break;
                            }
                        }

                        //find right edge
                        int right = x;
                        margin = 3;
                        for (int right_i = (y_low * width + right) * PixelSize; right_i < (y_low + 1) * width * PixelSize; right_i += PixelSize)
                        {
                            var rightPixel = imgBytes.AsSpan(right_i, 4);
                            if (PixelIsColor(rightPixel, targetColor.Value))
                            {
                                margin = 3;
                            }
                            else
                            {
                                margin--;
                            }

                            if (margin > 0)
                            {
                                right++;
                            } else
                            {
                                break;
                            }
                        }

                        //if wide enough, add to grid results
                        if (right - left > width / 24)
                        {
                            bool alreadyFound = false;
                            for (int j = 0; j < artifactListTuple.Count; j++)
                            {
                                //skip if start or end x, and y is close to an existing found artifactList
                                if ((artifactListTuple[j].StartX <= left && artifactListTuple[j].EndX >= left)
                                    || (artifactListTuple[j].StartX <= right && artifactListTuple[j].EndX >= right)
                                    || (left <= artifactListTuple[j].StartX && right >= artifactListTuple[j].StartX)
                                    || (left <= artifactListTuple[j].EndX && right >= artifactListTuple[j].EndX))
                                {
                                    if (Math.Abs(artifactListTuple[j].Item3 - y) < width * 0.07)
                                    {
                                        artifactListTuple[j] = (Math.Min(artifactListTuple[j].StartX, left),
                                            Math.Max(artifactListTuple[j].EndX, right), y);
                                        alreadyFound = true;
                                        break;
                                    }
                                }
                            }
                            if (!alreadyFound)
                            {
                                artifactListTuple.Add((left, right, y));
                            }
                        }
                    }

                    currStreak = 0;
                    targetColor = null;
                }


            }
            if (saveImages)
            {
                Marshal.Copy(imgBytes, 0, imgData.Scan0, numBytes);
                areaImg.UnlockBits(imgData);
            }

            List<Point> artifactListPoint = new List<Point>();
            foreach (var tup in artifactListTuple)
            {
                artifactListPoint.Add(new Point(coordOffset.X + (tup.EndX + tup.StartX) / 2, coordOffset.Y + tup.Y));
            }

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (saveImages)
            {
                using (Graphics g = Graphics.FromImage(areaImg))
                {
                    foreach (var tup in artifactListTuple)
                    {
                        g.FillRectangle(Brushes.Cyan, tup.StartX, tup.Y, tup.EndX - tup.StartX, 3);
                    }
                }
                areaImg.Save(Path.Join(Database.appDir, "images", "GenshinArtifactGridFiltered_" + timestamp + ".png"));
            }
            return artifactListPoint;
        }


        public static List<Point> equalizeGrid(List<Point> rawGrid, int rowTolerance, int colTolerance, out int rowCount)
        {
            List<List<Point>> rows = new List<List<Point>>();
            List<List<Point>> cols = new List<List<Point>>();
            //match point to row/col within tolerance, or create new if none found
            foreach (Point p in rawGrid)
            {
                bool hasRow = false;
                foreach (List<Point> row in rows)
                {
                    if (Math.Abs(p.Y - row[0].Y) < rowTolerance)
                    {
                        row.Add(p);
                        hasRow = true;
                        break;
                    }
                }
                if (!hasRow)
                {
                    rows.Add(new List<Point>());
                    rows.Last().Add(p);
                }

                bool hasCol = false;
                foreach (List<Point> col in cols)
                {
                    if (Math.Abs(p.X - col[0].X) < colTolerance)
                    {
                        col.Add(p);
                        hasCol = true;
                        break;
                    }
                }
                if (!hasCol)
                {
                    cols.Add(new List<Point>());
                    cols.Last().Add(p);
                }
            }

            //calc average coord for each row/col
            List<int> rowCoords = new List<int>();
            foreach (List<Point> row in rows)
            {
                int avgY = 0;
                foreach (Point p in row)
                {
                    avgY += p.Y;
                }
                avgY = avgY / row.Count;
                rowCoords.Add(avgY);
            }

            List<int> colCoords = new List<int>();
            foreach (List<Point> col in cols)
            {
                int avgX = 0;
                foreach (Point p in col)
                {
                    avgX += p.X;
                }
                avgX = avgX / col.Count;
                colCoords.Add(avgX);
            }


            rowCoords.Sort();
            colCoords.Sort();
            //create list of all grid points using row/col data
            List<Point> equalized = new List<Point>();
            foreach (int y in rowCoords)
            {
                foreach (int x in colCoords)
                {
                    equalized.Add(new Point(x, y));
                }
            }

            rowCount = rowCoords.Count;
            return equalized;
        }


        /// <summary>
        /// Find artifact area from an image of the backpack
        /// </summary>
        /// <param name="img">Full screenshot containing game</param>
        /// <param name="gameArea">Area of the screenshot containing only the game</param>
        /// <returns>Area of <paramref name="img"/> containing the artifact info</returns>
        public static Rectangle findArtifactArea(Bitmap img, Rectangle gameArea)
        {
            //Cut out relevant part of image
            gameArea = new Rectangle(gameArea.X + gameArea.Width / 2, gameArea.Y, gameArea.Width / 2, gameArea.Height);
            Bitmap areaImg = new Bitmap(gameArea.Width, gameArea.Height);
            using (Graphics g = Graphics.FromImage(areaImg))
            {
                g.DrawImage(img, 0, 0, gameArea, GraphicsUnit.Pixel);
            }

            int[] cols = new int[gameArea.Width];
            //prepare bytewise image processing
            BitmapData imgData = areaImg.LockBits(new Rectangle(0, 0, gameArea.Width, gameArea.Height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            areaImg.UnlockBits(imgData);
            int PixelSize = 4; //ARGB, reverse order

            var gameAreaWidth = gameArea.Width;
            var gameAreaHeight = gameArea.Height;

            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % gameAreaWidth;
                var pixel = imgBytes.AsSpan(i, 4);
                if (PixelIsColor(pixel, GameColor.PerfectBlack) || PixelIsColor(pixel, GameColor.VeryWhite)) //look for artifact name background colour
                {
                    cols[x]++;
                }
            }

            int streakLength = 0;
            int streakLocation = cols.Length - 1;
            bool isInStreak = false;

            for(int x = cols.Length - 1; x >= 0; x--)
            {
                if (cols[x] > gameAreaHeight * 0.25)
                {
                    if (isInStreak)
                    {
                        streakLength++;
                    } else
                    {
                        streakLength = 1;
                        streakLocation = x;
                        isInStreak = true;
                    }
                } 
                else
                {
                    if (isInStreak && streakLength > gameAreaHeight / 3)
                    {
                        break;
                    } else
                    {
                        isInStreak = false;
                    }
                }
            }

            if (!isInStreak || streakLocation - streakLength <= 0)
            {
                throw new Exception("No long enough streak, or streak ran into edge");
            }

            int rightmost = streakLocation;
            int leftmost = streakLocation - streakLength;

            int top = 0;
            int verticalBlackStreak = 0;
            for (int y = top; y < gameArea.Height - 1; y++)
            {
                int i = (y * gameArea.Width + leftmost + 1 ) * PixelSize;
                var pixel = imgBytes.AsSpan(i, 4);
                if (PixelIsColor(pixel, GameColor.PerfectBlack)) //look for artifact name background colour
                {
                    verticalBlackStreak++;

                    if (verticalBlackStreak > 10)
                    {
                        top = y - verticalBlackStreak;
                        break;
                    }
                } else
                {
                    verticalBlackStreak = 0;
                }
            }

            int verticalGrayStreak = 0;
            int bestGrayStreak = 0;
            int prelimBottom = top;

            // find preliminary bottom by looking for the longest streak of any form of gray (or black or white) starting from top
            for (int y = top; y < gameAreaHeight; y++)
            {
                int i = (y * gameArea.Width + leftmost + 1) * PixelSize;
                var pixel = imgBytes.AsSpan(i, 4);

                if (PixelIsColor(pixel, GameColor.AnyGray))
                {
                    verticalGrayStreak++;
                    if (verticalGrayStreak > bestGrayStreak)
                    {
                        bestGrayStreak = verticalGrayStreak;
                        prelimBottom = y;
                    }
                } 
                else
                {
                    verticalGrayStreak = 0;
                }
            }

            // then, improve the guess by taking the lowest white above the preliminary bottom
            // if this is note done, the capture will likely include some barely-transparent areas and screw up image hash for auto
            int bottom = prelimBottom;
            for (int y = prelimBottom; y > top; y--)
            {
                var found = false;
                for (int x = leftmost; x < rightmost; x++)
                {
                    int i = (y * gameArea.Width + x) * PixelSize;
                    var pixel = imgBytes.AsSpan(i, 4);

                    if (PixelIsColor(pixel, GameColor.VeryWhite))
                    {
                        bottom = y;
                        found = true;
                        break;
                    }
                }
                if (found)
                {
                    break;
                }
            }

            int height = bottom - top;

            return new Rectangle(gameArea.Left + leftmost, gameArea.Top + top, rightmost - leftmost, height);
        }

        /// <summary>
        /// Extract and filter the artifact area from an image of the backpack
        /// </summary>
        /// <param name="img">Full screenshot containing game</param>
        /// <param name="area">Area containing the artifact info</param>
        /// <param name="rows">Filter results per row</param>
        /// <returns>Filtered image of the artifact area</returns>
        public static Bitmap getWeaponImg(Bitmap img, Rectangle area, out int[] rows, bool saveImages, out bool locked, out Rectangle nameArea, out Rectangle statArea, out Rectangle refineArea, out Rectangle charArea)
        {
            nameArea = Rectangle.Empty;
            statArea = Rectangle.Empty;
            refineArea = Rectangle.Empty;
            charArea = Rectangle.Empty;

            locked = false;
            rows = new int[area.Height];
            //Get relevant part of image
            Bitmap areaImg = new Bitmap(area.Width, area.Height);
            using (Graphics g = Graphics.FromImage(areaImg))
            {
                g.DrawImage(img, 0, 0, area, GraphicsUnit.Pixel);
            }
            int width = areaImg.Width;
            int height = areaImg.Height;
            //Prepare bytewise image processing
            BitmapData imgData = areaImg.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            int PixelSize = 4; //ARGB, reverse order
            //some variables to keep track of which part of the image we are in
            int section = 0; //0 = Name, 1 = Stats, 2 = Lock, 3 = Refinement text, 4 = character
            int sectionStart = 0;
            int sectionEnd = 0;
            int rightEdge = 0;
            int leftEdge = width - 1;
            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % width;
                int y = (i / PixelSize - x) / width;
                int y_below = Math.Min(((y + 1) * width + x) * PixelSize, numBytes - PixelSize - 1);
                var pixel = imgBytes.AsSpan(i, 4);
                var pixelBelow = imgBytes.AsSpan(y_below, 4);
                if (
                    (section == 0 && x < width && PixelIsColor(pixel, GameColor.TextWhiteIsh)) //look for white-ish text
                    || (section == 1 && x < width * 0.55 && (PixelIsColor(pixel, GameColor.TextBrightWhite) || (PixelIsColor(pixelBelow, GameColor.TextBrightWhite)))) //look for bright white text, skip right edge
                    || (section == 3 && PixelIsColor(pixel, GameColor.TextGold)) //look for "Gold"
                    || ((section == 4) && x > width * 0.15 && PixelIsColor(pixel, GameColor.TextBlackIsh)) // look for black, skip left edge (character head)
                    )
                {
                    //Make Black
                    imgBytes[i] = 0;
                    imgBytes[i + 1] = 0;
                    imgBytes[i + 2] = 0;
                    imgBytes[i + 3] = 255;
                    rows[y]++;
                    if (x > rightEdge)
                        rightEdge = x;
                    if (x < leftEdge && x != 0)
                        leftEdge = x;


                    if (section == 0)
                    {
                        if (sectionEnd != 0 && y > sectionEnd)
                        {
                            //found white after gap
                            nameArea = new Rectangle(0, sectionStart, width, sectionEnd - sectionStart);
                            section = 1; 
                            //make white
                            imgBytes[i] = 255;
                            imgBytes[i + 1] = 255;
                            imgBytes[i + 2] = 255;
                            imgBytes[i + 3] = 255;
                        } 
                        else if (sectionStart == 0)
                        {
                            //first row of name text
                            sectionStart = y;
                        } 
                        else
                        {
                            //advance end row of name text
                            sectionEnd = y + 3;
                        }
                    } else 
                    if (section == 3)
                    {
                        if (y < sectionEnd && y + 3 != sectionEnd)
                        {
                            sectionEnd = Math.Min(y + 3, height - 1);
                        }
                    }
                }
                else
                {
                    if (section == 2 && PixelIsColor(pixel, GameColor.LockRed))
                    {
                        //if section 2, look for red lock
                        locked = true;
                    }
                    else if (section == 2 && PixelIsColor(pixel, GameColor.TextGold))
                    {
                        //if section 2, look for "Gold" text
                        sectionStart = Math.Max(0, y - 3);
                        sectionEnd = Math.Min(y + 3, height - 1);
                        section = 3;

                        //Make Black and continue
                        imgBytes[i] = 0;
                        imgBytes[i + 1] = 0;
                        imgBytes[i + 2] = 0;
                        imgBytes[i + 3] = 255;
                        continue;
                    }
                    else if (section == 3 && PixelIsColor(pixel, GameColor.BackgroundCharacterArea))
                    {
                        // if section 3, look for yellow-white-ish for character area
                        refineArea = new Rectangle(0, sectionStart, width, sectionEnd - sectionStart);
                        charArea = new Rectangle(0, y, width, height - y);
                        section = 4;
                    }
                    //Make White
                    imgBytes[i] = 255;
                    imgBytes[i + 1] = 255;
                    imgBytes[i + 2] = 255;
                    imgBytes[i + 3] = 255;
                }

                if (x == 0)
                {
                    if (section == 1)
                    {
                        //check if coming row is white-ish, if so move to section 2
                        int tmp = (y * width + (int)(width * 0.05)) * PixelSize;
                        var tmpPixel = imgBytes.AsSpan(tmp, 4);
                        if (PixelIsColor(tmpPixel, GameColor.BackgroundWhiteIsh))
                        {
                            //Make White
                            imgBytes[i] = 255;
                            imgBytes[i + 1] = 255;
                            imgBytes[i + 2] = 255;
                            imgBytes[i + 3] = 255;

                            statArea = new Rectangle(0, nameArea.Bottom, (int)(width * 0.55), y - nameArea.Bottom);
                            section = 2;
                            i += width * PixelSize;
                        }

                    }
                }
            }

            if (section == 3) // reached passive but never reached character region
            {
                refineArea = new Rectangle(0, sectionStart, width, sectionEnd - sectionStart);
            }
            Marshal.Copy(imgBytes, 0, imgData.Scan0, numBytes);
            areaImg.UnlockBits(imgData);

            //Bitmap thinImg = new Bitmap(rightEdge - leftEdge, height);
            //using (Graphics g = Graphics.FromImage(thinImg))
            //{
            //    g.DrawImage(areaImg, 0, 0, new Rectangle(leftEdge, 0, rightEdge - leftEdge, height), GraphicsUnit.Pixel);
            //}
            //areaImg = thinImg;

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (saveImages)
                areaImg.Save(Path.Join(Database.appDir, "images", "GenshinArtifactImgFiltered_" + timestamp + ".png"));
            return areaImg;
        }


        /// <summary>
        /// Extract and filter the artifact area from an image of the backpack
        /// </summary>
        /// <param name="img">Full screenshot containing game</param>
        /// <param name="area">Area containing the artifact info</param>
        /// <param name="rows">Filter results per row</param>
        /// <returns>Filtered image of the artifact area</returns>
        public static Bitmap getDiscImg(Bitmap img, Rectangle area, out int[] rows, bool saveImages)
        {
            rows = new int[area.Height];
            //Get relevant part of image
            Bitmap areaImg = new Bitmap(area.Width, area.Height);
            using (Graphics g = Graphics.FromImage(areaImg))
            {
                g.DrawImage(img, 0, 0, area, GraphicsUnit.Pixel);
            }
            int width = areaImg.Width;
            int height = areaImg.Height;

            bool fullBlackRow = false;
            bool hasSeenWhite = false;
            bool hasSeenAllBlackAfterWhite = false;

            bool SlopedColorThreshold(int pX, int pY, Span<byte> pixel)
            {
                bool pixelIsWhite = PixelIsColor(pixel, GameColor.VeryWhite);

                if (!hasSeenWhite)
                {
                    hasSeenWhite = pixelIsWhite;
                }
                else if (!hasSeenAllBlackAfterWhite)
                {
                    // if we've seen any white, but no fully black line yet
                    // apply a diagonal line to cut out the image for the disc itself
                    // this allows looser white color threshold without the disc image adding noise
                    var pixelIsBlack = PixelIsColor(pixel, GameColor.PerfectBlack);
                    if (pX < width * 0.01)
                    {
                        fullBlackRow = pixelIsBlack;
                    }
                    else
                    {
                        fullBlackRow &= pixelIsBlack;
                    }

                    if (pX > width * 0.99 && fullBlackRow)
                    {
                        hasSeenAllBlackAfterWhite = true;
                    }

                    var discBorderLine = width * 0.7 - 0.5 * pY;

                    if (pX > discBorderLine)
                    {
                        return false;
                    }
                }

                return pixelIsWhite;
            }

            //Prepare bytewise image processing
            BitmapData imgData = areaImg.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, areaImg.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            int PixelSize = 4; //ARGB, reverse order            
            for (int i = 0; i < numBytes; i += PixelSize)
            {
                int x = (i / PixelSize) % width;
                int y = (i / PixelSize - x) / width;
                var pixel = imgBytes.AsSpan(i, 4);
                if (SlopedColorThreshold(x, y, pixel)) // Make the white text black and everything else white
                {
                    rows[y]++;
                    imgBytes[i] = 0;
                    imgBytes[i + 1] = 0;
                    imgBytes[i + 2] = 0;
                    imgBytes[i + 3] = 255;
                }
                else
                {
                    imgBytes[i] = 255;
                    imgBytes[i + 1] = 255;
                    imgBytes[i + 2] = 255;
                    imgBytes[i + 3] = 255;
                }
            }
            Marshal.Copy(imgBytes, 0, imgData.Scan0, numBytes);
            areaImg.UnlockBits(imgData);

            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (saveImages)
                areaImg.Save(Path.Join(Database.appDir, "images", "GenshinArtifactImgFiltered_" + timestamp + ".png"));
            return areaImg;
        }

        /// <summary>
        /// Capture screenshot of main screen
        /// </summary>
        /// <returns>Screenshot of main screen</returns>
        public static Bitmap CaptureScreenshot(bool saveImages, Rectangle area, bool useArea = false )
        {
            if (!useArea)
                area = new Rectangle(0, 0, Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);

            Bitmap img = new Bitmap(area.Width, area.Height);
            Size areaSize = new Size(area.Width, area.Height);
            using (Graphics g = Graphics.FromImage(img))
            {
                g.CopyFromScreen(area.X, area.Y, 0, 0, areaSize);
            }
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (saveImages)
                img.Save(Path.Join(Database.appDir, "images", "GenshinScreen_" + timestamp + ".png"));
            return img;
        }

        /// <summary>
        /// Find approximate area containing the game
        /// </summary>
        /// <param name="full">Screenshot of main monitor</param>
        /// <returns>Area containing game</returns>
        public static Rectangle? findGameArea(Bitmap full)
        {
            //Prepare bytewise image processing

            int fullHeight = full.Height;
            int fullWidth = full.Width;
            BitmapData imgData = full.LockBits(new Rectangle(0, 0, fullWidth, fullHeight), ImageLockMode.ReadWrite, full.PixelFormat);
            int numBytes = Math.Abs(imgData.Stride) * imgData.Height;
            byte[] imgBytes = new byte[numBytes];
            Marshal.Copy(imgData.Scan0, imgBytes, 0, numBytes);
            int PixelSize = 4; //ARGB, reverse order
            full.UnlockBits(imgData);

            int minWidth = Screen.PrimaryScreen.Bounds.Width / 4;

            int x = fullWidth / 2; //probing via middle of screen, looking for white window header
            for (int y = fullHeight / 2; y > 0; y--)
            {
                int i_pos = 0;
                int i_neg = 0;
                int index = (y * fullWidth + x) * PixelSize;
                var pixel = imgBytes.AsSpan(index, 4);

                //explore white area right
                while (x + i_pos < fullWidth * 0.99 && PixelIsColor(pixel, GameColor.WhiteWindowHeader))
                {
                    i_pos++;
                    index = (y * fullWidth + x + i_pos) * PixelSize;
                    pixel = imgBytes.AsSpan(index, 4);
                }

                if (i_pos == 0)
                    continue;

                index = (y * fullWidth + x) * PixelSize;
                pixel = imgBytes.AsSpan(index, 4);
                //explore white area left
                while (x - i_neg > fullWidth * 0.01 && PixelIsColor(pixel, GameColor.WhiteWindowHeader))
                {
                    i_neg++;
                    index = (y * fullWidth + x - i_neg) * PixelSize;
                    pixel = imgBytes.AsSpan(index, 4);
                }

                //check if feasible game window size
                if (i_pos + i_neg < minWidth)
                    continue;

                int top = y + 1;
                int left = x - i_neg;
                int width = (i_pos + i_neg);

                //find bottom
                int height = fullHeight - y - 1;
                while (height > 10)
                {
                    int row = 0;
                    int currStreak = 0;
                    int maxStreak = 0;
                    for (int i = 0; i < width * 0.3; i++)
                    {
                        index = ((y + height) * fullWidth + left + i) * PixelSize;
                        pixel = imgBytes.AsSpan(index, 4);
                        if (PixelIsColor(pixel, GameColor.PerfectBlack))
                        {
                            row++;
                            currStreak++;
                            if (currStreak > maxStreak)
                                maxStreak = currStreak;
                        }
                        else
                        {
                            currStreak = 0;
                        }
                    }

                    if (row > width * 0.3 * 0.65 && maxStreak > width * 0.3 * 0.25)
                        break;

                    height--;
                }
                return new Rectangle(left, top, width, height);
            }
            return null;
        }

        /// <summary>
        /// Select and load screenshot, taken from WFInfo but heavily cut down
        /// </summary>
        /// <returns>Loaded screenshot, or empty 1x1 bitmap on failure</returns>
        public static Bitmap LoadScreenshot()
        {
            Bitmap img = new Bitmap(1, 1);
            // Using WinForms for the openFileDialog because it's simpler and much easier
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                openFileDialog.Filter = "image files (*.png)|*.png|All files (*.*)|*.*";
                openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;
                openFileDialog.Multiselect = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        foreach (string file in openFileDialog.FileNames)
                        {
                            img = new Bitmap(file);
                            break;
                        }
                    }
                    catch (Exception)
                    {

                    }
                }
            }
            return img;
        }


        /// <summary>
        /// Use OCR to read text from row
        /// </summary>
        /// <param name="img">Image to read, filtered</param>
        /// <param name="start">Starting row of <paramref name="img"/> to read</param>
        /// <param name="stop">Ending row of <paramref name="img"/> to read</param>
        /// <param name="validText">List of words to match against</param>
        /// <param name="dist">Levenshtein distance to closest match</param>
        /// <param name="rawText">Raw result from OCR process (appended to <paramref name="prevRaw"/>)</param>
        /// <param name="prevRaw">String to append in front of raw OCR result before searching for match</param>
        /// <returns>Closest matching word</returns>
        public static string OCRRow<T>(Bitmap img, int start, int stop, List<T> validText, out T? result, out int dist, out string rawText, string prevRaw, bool saveImages, TesseractEngine tessEngine) where T: struct, IParsableData
        {

            //tessEngine.SetVariable("tessedit_char_whitelist", @"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ9876543210+%,:() ");
            //Copy relevant part of image
            int height = stop - start;
            Bitmap scanArea = new Bitmap(img.Width, height);
            using (Graphics g = Graphics.FromImage(scanArea))
            {
                Rectangle sourceRect = new Rectangle(0, start, img.Width, height);
                g.DrawImage(img, 0, 0, sourceRect, GraphicsUnit.Pixel);
            }
            scanArea.SetResolution(96, 96); //make sure DPI doesn't affect OCR results


            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm-ssff");
            if (saveImages)
                scanArea.Save(Path.Join(Database.appDir, "images", "GenshinTextRow_" + timestamp + ".png"));

            //Do OCR and append to prevRaw
            string text = prevRaw;
            using (var page = tessEngine.Process(scanArea, PageSegMode.SingleBlock))
            {
                using (var iterator = page.GetIterator())
                {
                    iterator.Begin();
                    do
                    {
                        text += iterator.GetText(PageIteratorLevel.TextLine);
                    } while (iterator.Next(PageIteratorLevel.TextLine));
                }
            }
            //mild filtering
            text = Regex.Replace(text, @"\s+", "");
            rawText = text;

            string bestMatch = Database.FindClosestMatch(text, validText, out result, out dist);
            //Debug.WriteLine("Got (" + dist + ") \"" + bestMatch + "\" from \"" + text + "\"");

            return bestMatch;
        }

        public static string OCRRowSimple(Bitmap img, int start, int stop, out string rawText, string prevRaw, bool saveImages, TesseractEngine tessEngine)
        {
            var dummy = new List<SimpleParsable>();
            return OCRRow(img, start, stop, dummy, out _, out _, out rawText, prevRaw, saveImages, tessEngine);
        }

        /// <summary>
        /// Extract stats from image to window text boxes
        /// </summary>
        /// <param name="img">Image of artifact area, filtered</param>
        /// <param name="rows">Filter results per row</param>
        public static Disc getDisc(Bitmap img, int[] rows, bool saveImages, TesseractEngine tessEngine)
        {
            //get all potential text rows
            List<Tuple<int, int>> textRows = new List<Tuple<int, int>>();
            int i = 0;
            int height = img.Height;
            int width = img.Width;
            while (i + 1 < img.Height)
            {
                while (i + 1 < height && rows[i] == 0)
                    i++;
                int rowTop = i;
                while (i + 1 < height && !(rows[i] == 0))
                    i++;
                textRows.Add(Tuple.Create(Math.Max(0, rowTop - 3), Math.Min(height - 1, i + 3)));
            }

            var disc = new Disc();

            string prevRaw = "";
            i = 0;
            //Set and slot
            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, Database.DiscSets, out DiscSetAndSlot? bestMatch, out int dist, out string rawText, prevRaw, saveImages, tessEngine);
                prevRaw = rawText;
                if (bestMatch.HasValue && dist < 3 && (rawText.Contains('[') || rawText.Contains(']')))
                {
                    disc.slot = bestMatch.Value;
                    i++;
                    break;
                }
            }

            //Level and rarity
            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(img, textRows[i].Item1, textRows[i].Item2, Database.DiscLevels, out DiscLevelAndRarity? bestMatch, out int dist, out string rawText, "", saveImages, tessEngine);
                
                if (bestMatch.HasValue && dist < 2)
                {
                    disc.level = bestMatch.Value;
                    i++;
                    break;
                }
            }

            if (!disc.level.HasValue || !disc.slot.HasValue)
            {
                return disc;
            }

            int rarity = (int)disc.level.Value.Tier;

            // cut out the numbers from image for use with main stat.
            using Bitmap narrowImage = new Bitmap(img);
            using (Graphics g = Graphics.FromImage(narrowImage))
            {
                g.FillRectangle(Brushes.White, new Rectangle((int)(width * 0.75), 0, width, height));
            }

            // cut out the numbers from image for use with main stat.
            using Bitmap narrowImageEnd = new Bitmap(img);
            using (Graphics g = Graphics.FromImage(narrowImageEnd))
            {
                g.FillRectangle(Brushes.White, new Rectangle(0, 0, (int)(width * 0.75), height));
            }


            //Main stat

            for (; i < textRows.Count; i++)
            {
                string result = OCRRow(narrowImage, textRows[i].Item1, textRows[i].Item2, Database.DiscMainStats[disc.slot.Value.Slot], out DiscMainStat? bestMatch, out int dist, out string rawText, "", saveImages, tessEngine);
                if (bestMatch.HasValue && rawText.Length != 0)
                {
                    disc.main = bestMatch.Value;
                    i++;
                    break;
                }
            }

            //Substats
            int substat = 0;
            for (; i < textRows.Count; i++)
            {
                _ = OCRRow(narrowImage, textRows[i].Item1, textRows[i].Item2, Database.rarityData[rarity].DiscSubStats, out _, out _, out string rawText1, "", saveImages, tessEngine);

                tessEngine.SetVariable("tessedit_char_whitelist", @"9876543210.%");
                string result = OCRRow(narrowImageEnd, textRows[i].Item1, textRows[i].Item2, Database.rarityData[rarity].DiscSubStats, out DiscSubStat? bestMatch, out int dist, out string rawText, rawText1, saveImages, tessEngine);
                tessEngine.SetVariable("tessedit_char_whitelist", @"");

                if (bestMatch.HasValue && dist < 3)
                {
                    disc.subs.Add(bestMatch.Value);
                    if (substat > 2)
                    {
                        i++;
                        break;
                    }
                    substat++;
                }
                else if (rawText.Length > 5)
                {
                    break;
                }
            }


            return disc;
        }

        public enum GameColor
        {
            TextWhiteIsh, // artifact area background color
            TextBrightWhite, // used for artifact level
            TextBlackIsh, // substats, equipped status
            TextGreen, // set text
            TextGold, // weapon refinement text

            BackgroundCharacterArea, // character label background
            BackgroundWhiteIsh, // background of area with substats, description etc
            BackgroundArtifactName, // background for artifact name, 5 star or 4 star
            BackgroundGridLabel, // label with white background and black text below items in inventory

            WhiteWindowHeader, // White windows window header

            StarYellow, // rarity star
            LockRed, // lock icon

            PerfectBlack, // Disc card background
            VeryWhite, // Disc card text, fuzzy
            PerfectWhite, // Disc card text
            AnyGray, // Any form of black/white/gray

            ArtifactLabelB, // B rarity item label in grid
            ArtifactLabelA, // A rarity item label in grid
            ArtifactLabelS, // S rarity item label in grid
        }

        /// <summary>
        /// Check pixel against a defined color profile
        /// </summary>
        /// <param name="pixel">span of size 4, containing the BGRA color bytes of a pixel</param>
        /// <param name="color"></param>
        /// <returns></returns>
        public static bool PixelIsColor(Span<byte> pixel, GameColor color)
        {
            switch (color)
            {
                case GameColor.TextWhiteIsh:
                    return pixel[0] > 140 && pixel[1] > 140 && pixel[2] > 140;
                case GameColor.TextBrightWhite:
                    return pixel[0] > 225 && pixel[1] > 225 && pixel[2] > 225;
                case GameColor.TextBlackIsh:
                    return pixel[0] < 150 && pixel[1] < 150 && pixel[2] < 150;
                case GameColor.TextGreen:
                    return (pixel[0] < 130 && pixel[1] > 160 && pixel[2] < 130);
                case GameColor.TextGold:
                    return (pixel[0] is > 100 and < 160 && pixel[1] > 160 && pixel[2] is > 200 and < 230);

                case GameColor.BackgroundCharacterArea:
                    return pixel[0] is > 180 and < 200 && pixel[1] > 220 && pixel[2] > 240;
                case GameColor.BackgroundWhiteIsh:
                    return pixel[0] is > 200 and < 240 && pixel[1] is > 200 and < 240 && pixel[2] is > 200 and < 240;
                case GameColor.BackgroundArtifactName:
                    return (pixel[0] is > 40 and < 60 && pixel[1] is > 90 and < 110 && pixel[2] is > 180 and < 200)
                        || (pixel[0] is > 220 and < 230 && pixel[1] is > 80 and < 90 && pixel[2] is > 155 and < 165);
                case GameColor.BackgroundGridLabel:
                    return (pixel[0] > 200 && pixel[1] > 200 && pixel[2] > 200)
                        || (pixel[0] is > 65 and < 110 && pixel[1] is > 65 and < 110 && pixel[2] is > 65 and < 110 && pixel[3] is > 65 and < 100);

                case GameColor.WhiteWindowHeader:
                    return pixel[0] > 220 && pixel[1] > 220 && pixel[2] > 220;

                case GameColor.StarYellow:
                    return pixel[0] < 60 && pixel[1] > 190 && pixel[2] > 240;
                case GameColor.LockRed:
                    return pixel[0] < 150 && pixel[1] > 120 && pixel[2] > 200;


                case GameColor.PerfectBlack:
                    return pixel[0] == 0 && pixel[1] == 0 && pixel[2] == 0;
                case GameColor.VeryWhite:
                    return pixel[0] == pixel[1] && pixel[1] == pixel[2] && pixel[2] > 230;
                case GameColor.PerfectWhite:
                    return pixel[0] == pixel[1] && pixel[1] == pixel[2] && pixel[2] == 255;
                case GameColor.AnyGray:
                    return pixel[0] == pixel[1] && pixel[1] == pixel[2];


                case GameColor.ArtifactLabelB:
                    return pixel[0] == 255 && pixel[1] == 169 && pixel[2] == 0;
                case GameColor.ArtifactLabelA:
                    return pixel[0] == 255 && pixel[1] == 0 && pixel[2] == 233;
                case GameColor.ArtifactLabelS:
                    return pixel[0] == 0 && pixel[1] == 181 && pixel[2] == 255;
            }
            
            throw new NotImplementedException("No filter defined for GameColor " + color);
        }
    }
}
