﻿using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Drawing;
using System.ComponentModel;

namespace ExifLibrary
{
    /// <summary>
    /// Reads and writes Exif data contained in a JPEG/Exif file.
    /// </summary>
    [TypeDescriptionProvider(typeof(ExifFileTypeDescriptionProvider))]
    public class ExifFile
    {
        #region Member Variables
        private JPEGFile file;
        private JPEGSection jfifApp0;
        private JPEGSection jfxxApp0;
        private JPEGSection exifApp1;
        private uint makerNoteOffset;
        private long exifIFDFieldOffset, gpsIFDFieldOffset, interopIFDFieldOffset, firstIFDFieldOffset;
        private long thumbOffsetLocation, thumbSizeLocation;
        private uint thumbOffsetValue, thumbSizeValue;
        private bool makerNoteProcessed;
        #endregion

        #region Properties
        /// <summary>
        /// Gets the collection of Exif properties contained in the JPEG file.
        /// </summary>
        public ExifPropertyCollection Properties { get; private set; }
        /// <summary>
        /// Gets or sets the byte-order of the Exif properties.
        /// </summary>
        public BitConverterEx.ByteOrder ByteOrder { get; set; }
        /// <summary>
        /// Gets or sets the embedded thumbnail image.
        /// </summary>
        public JPEGFile Thumbnail { get; set; }
        /// <summary>
        /// Gets or sets the Exif property with the given key.
        /// </summary>
        /// <param name="key">The Exif tag associated with the Exif property.</param>
        public ExifProperty this[ExifTag key]
        {
            get
            {
                return Properties[key];
            }
            set
            {
                Properties[key] = value;
            }
        }
        #endregion

        #region Constructors
        protected ExifFile()
        {
            ;
        }
        #endregion

        #region Instance Methods
        /// <summary>
        /// Saves the JPEG/Exif image with the given filename.
        /// </summary>
        /// <param name="filename">The complete path to the JPEG/Exif file.</param>
        public void Save(string filename)
        {
            Save(filename, true);
        }

        /// <summary>
        /// Saves the JPEG/Exif image with the given filename.
        /// </summary>
        /// <param name="filename">The complete path to the JPEG/Exif file.</param>
        /// <param name="preserveMakerNote">Determines whether the maker note offset of
        /// the original file will be preserved.</param>
        public void Save(string filename, bool preserveMakerNote)
        {
            WriteJFIFApp0();
            WriteJFXXApp0();
            WriteExifApp1(preserveMakerNote);
            file.Save(filename);
        }

        /// <summary>
        /// Returns a System.Drawing.Image created with image data.
        /// </summary>
        public Image ToImage()
        {
            return file.ToImage();
        }
        #endregion

        #region Private Helper Methods
        /// <summary>
        /// Reads the APP0 section containing JFIF metadata.
        /// </summary>
        private void ReadJFIFAPP0()
        {
            // Find the APP0 section containing JFIF metadata
            jfifApp0 = file.Sections.Find(a => (a.Marker == JPEGMarker.APP0) &&
                a.Header.Length >= 5 &&
                (Encoding.ASCII.GetString(a.Header, 0, 5) == "JFIF\0"));

            // If there is no APP0 section, return.
            if (jfifApp0 == null)
                return;

            byte[] header = jfifApp0.Header;
            BitConverterEx jfifConv = BitConverterEx.BigEndian;

            // Version
            ushort version = jfifConv.ToUInt16(header, 5);
            Properties.Add(new JFIFVersion(ExifTag.JFIFVersion, version));

            // Units
            byte unit = header[7];
            Properties.Add(new ExifEnumProperty<JFIFDensityUnit>(ExifTag.JFIFUnits, (JFIFDensityUnit)unit));

            // X and Y densities
            ushort xdensity = jfifConv.ToUInt16(header, 8);
            Properties.Add(new ExifUShort(ExifTag.XDensity, xdensity));
            ushort ydensity = jfifConv.ToUInt16(header, 10);
            Properties.Add(new ExifUShort(ExifTag.YDensity, ydensity));

            // Thumbnails pixel count
            byte xthumbnail = header[12];
            Properties.Add(new ExifByte(ExifTag.JFIFXThumbnail, xthumbnail));
            byte ythumbnail = header[13];
            Properties.Add(new ExifByte(ExifTag.JFIFYThumbnail, ythumbnail));

            // Read JFIF thumbnail
            int n = xthumbnail * ythumbnail;
            byte[] jfifThumbnail = new byte[n];
            Array.Copy(header, 14, jfifThumbnail, 0, n);
            Properties.Add(new JFIFThumbnailProperty(ExifTag.JFIFThumbnail, new JFIFThumbnail(JFIFThumbnail.ImageFormat.JPEG, jfifThumbnail)));
        }
        /// <summary>
        /// Replaces the contents of the APP0 section with the JFIF properties.
        /// </summary>
        private bool WriteJFIFApp0()
        {
            // Which IFD sections do we have?
            List<ExifProperty> ifdjfef = new List<ExifProperty>();
            foreach (ExifProperty prop in Properties)
            {
                if (prop.IFD == IFD.JFIF)
                    ifdjfef.Add(prop);
            }

            if (ifdjfef.Count == 0)
            {
                // Nothing to write
                return false;
            }

            BitConverterEx jfifConv = BitConverterEx.BigEndian;

            // Create a memory stream to write the APP0 section to
            MemoryStream ms = new MemoryStream();

            // JFIF identifer
            ms.Write(Encoding.ASCII.GetBytes("JFIF\0"), 0, 5);

            // Write tags
            foreach (ExifProperty prop in ifdjfef)
            {
                ExifInterOperability interop = prop.Interoperability;
                byte[] data = interop.Data;
                if (BitConverterEx.SystemByteOrder != BitConverterEx.ByteOrder.BigEndian && interop.TypeID == 3)
                    Array.Reverse(data);
                ms.Write(data, 0, data.Length);
            }

            ms.Close();

            // Return APP0 header
            jfifApp0.Header = ms.ToArray();
            return true;
        }
        /// <summary>
        /// Reads the APP0 section containing JFIF extension metadata.
        /// </summary>
        private void ReadJFXXAPP0()
        {
            // Find the APP0 section containing JFIF metadata
            jfxxApp0 = file.Sections.Find(a => (a.Marker == JPEGMarker.APP0) &&
                a.Header.Length >= 5 &&
                (Encoding.ASCII.GetString(a.Header, 0, 5) == "JFXX\0"));

            // If there is no APP0 section, return.
            if (jfxxApp0 == null)
                return;

            byte[] header = jfxxApp0.Header;
            BitConverterEx jfifConv = BitConverterEx.BigEndian;

            // Version
            JFIFExtension version = (JFIFExtension)header[5];
            Properties.Add(new ExifEnumProperty<JFIFExtension>(ExifTag.JFXXExtensionCode, version));

            // Read thumbnail
            if (version == JFIFExtension.ThumbnailJPEG)
            {
                byte[] data = new byte[header.Length - 6];
                Array.Copy(header, 6, data, 0, data.Length);
                Properties.Add(new JFIFThumbnailProperty(ExifTag.JFXXThumbnail, new JFIFThumbnail(JFIFThumbnail.ImageFormat.JPEG, data)));
            }
            else if (version == JFIFExtension.Thumbnail24BitRGB)
            {
                // Thumbnails pixel count
                byte xthumbnail = header[6];
                Properties.Add(new ExifByte(ExifTag.JFXXXThumbnail, xthumbnail));
                byte ythumbnail = header[7];
                Properties.Add(new ExifByte(ExifTag.JFXXYThumbnail, ythumbnail));
                byte[] data = new byte[3 * xthumbnail * ythumbnail];
                Array.Copy(header, 8, data, 0, data.Length);
                Properties.Add(new JFIFThumbnailProperty(ExifTag.JFXXThumbnail, new JFIFThumbnail(JFIFThumbnail.ImageFormat.BMP24Bit, data)));
            }
            else if (version == JFIFExtension.ThumbnailPaletteRGB)
            {
                // Thumbnails pixel count
                byte xthumbnail = header[6];
                Properties.Add(new ExifByte(ExifTag.JFXXXThumbnail, xthumbnail));
                byte ythumbnail = header[7];
                Properties.Add(new ExifByte(ExifTag.JFXXYThumbnail, ythumbnail));
                byte[] palette = new byte[768];
                Array.Copy(header, 8, palette, 0, palette.Length);
                byte[] data = new byte[xthumbnail * ythumbnail];
                Array.Copy(header, 8 + 768, data, 0, data.Length);
                Properties.Add(new JFIFThumbnailProperty(ExifTag.JFXXThumbnail, new JFIFThumbnail(palette, data)));
            }
        }
        /// <summary>
        /// Replaces the contents of the APP0 section with the JFIF extension properties.
        /// </summary>
        private bool WriteJFXXApp0()
        {
            // Which IFD sections do we have?
            List<ExifProperty> ifdjfef = new List<ExifProperty>();
            foreach (ExifProperty prop in Properties)
            {
                if (prop.IFD == IFD.JFXX)
                    ifdjfef.Add(prop);
            }

            if (ifdjfef.Count == 0)
            {
                // Nothing to write
                return false;
            }

            BitConverterEx jfifConv = BitConverterEx.BigEndian;

            // Create a memory stream to write the APP0 section to
            MemoryStream ms = new MemoryStream();

            // JFIF identifer
            ms.Write(Encoding.ASCII.GetBytes("JFXX\0"), 0, 5);

            // Write tags
            foreach (ExifProperty prop in ifdjfef)
            {
                ExifInterOperability interop = prop.Interoperability;
                byte[] data = interop.Data;
                if (BitConverterEx.SystemByteOrder != BitConverterEx.ByteOrder.BigEndian && interop.TypeID == 3)
                    Array.Reverse(data);
                ms.Write(data, 0, data.Length);
            }

            ms.Close();

            // Return APP0 header
            jfxxApp0.Header = ms.ToArray();
            return true;
        }

        /// <summary>
        /// Reads the APP1 section containing Exif metadata.
        /// </summary>
        private void ReadExifAPP1()
        {
            // Find the APP1 section containing Exif metadata
            exifApp1 = file.Sections.Find(a => (a.Marker == JPEGMarker.APP1) &&
                a.Header.Length >= 6 &&
                (Encoding.ASCII.GetString(a.Header, 0, 6) == "Exif\0\0"));

            // If there is no APP1 section, add a new one after the last APP0 section (if any).
            if (exifApp1 == null)
            {
                int insertionIndex = file.Sections.FindLastIndex(a => a.Marker == JPEGMarker.APP0);
                if (insertionIndex == -1) insertionIndex = 0;
                insertionIndex++;
                exifApp1 = new JPEGSection(JPEGMarker.APP1);
                file.Sections.Insert(insertionIndex, exifApp1);
                if (BitConverterEx.SystemByteOrder == BitConverterEx.ByteOrder.LittleEndian)
                    ByteOrder = BitConverterEx.ByteOrder.LittleEndian;
                else
                    ByteOrder = BitConverterEx.ByteOrder.BigEndian;
                return;
            }

            byte[] header = exifApp1.Header;
            SortedList<int, IFD> ifdqueue = new SortedList<int, IFD>();
            makerNoteOffset = 0;

            // TIFF header
            int tiffoffset = 6;
            if (header[tiffoffset] == 0x49 && header[tiffoffset + 1] == 0x49)
                ByteOrder = BitConverterEx.ByteOrder.LittleEndian;
            else if (header[tiffoffset] == 0x4D && header[tiffoffset + 1] == 0x4D)
                ByteOrder = BitConverterEx.ByteOrder.BigEndian;
            else
                throw new NotValidExifFileException();

            // TIFF header may have a different byte order
            BitConverterEx.ByteOrder tiffByteOrder = ByteOrder;
            if (BitConverterEx.LittleEndian.ToUInt16(header, tiffoffset + 2) == 42)
                tiffByteOrder = BitConverterEx.ByteOrder.LittleEndian;
            else if (BitConverterEx.BigEndian.ToUInt16(header, tiffoffset + 2) == 42)
                tiffByteOrder = BitConverterEx.ByteOrder.BigEndian;
            else
                throw new NotValidExifFileException();

            // Offset to 0th IFD
            int ifd0offset = (int)BitConverterEx.ToUInt32(header, tiffoffset + 4, tiffByteOrder, BitConverterEx.ByteOrder.System);
            ifdqueue.Add(ifd0offset, IFD.Zeroth);

            BitConverterEx conv = new BitConverterEx(ByteOrder, BitConverterEx.ByteOrder.System);
            int thumboffset = -1;
            int thumblength = 0;
            int thumbtype = -1;
            // Read IFDs
            while (ifdqueue.Count != 0)
            {
                int ifdoffset = tiffoffset + ifdqueue.Keys[0];
                IFD currentifd = ifdqueue.Values[0];
                ifdqueue.RemoveAt(0);

                // Field count
                ushort fieldcount = conv.ToUInt16(header, ifdoffset);
                for (short i = 0; i < fieldcount; i++)
                {
                    // Read field info
                    int fieldoffset = ifdoffset + 2 + 12 * i;
                    ushort tag = conv.ToUInt16(header, fieldoffset);
                    ushort type = conv.ToUInt16(header, fieldoffset + 2);
                    uint count = conv.ToUInt32(header, fieldoffset + 4);
                    byte[] value = new byte[4];
                    Array.Copy(header, fieldoffset + 8, value, 0, 4);

                    // Fields containing offsets to other IFDs
                    if (currentifd == IFD.Zeroth && tag == 0x8769)
                    {
                        int exififdpointer = (int)conv.ToUInt32(value, 0);
                        ifdqueue.Add(exififdpointer, IFD.EXIF);
                    }
                    else if (currentifd == IFD.Zeroth && tag == 0x8825)
                    {
                        int gpsifdpointer = (int)conv.ToUInt32(value, 0);
                        ifdqueue.Add(gpsifdpointer, IFD.GPS);
                    }
                    else if (currentifd == IFD.EXIF && tag == 0xa005)
                    {
                        int interopifdpointer = (int)conv.ToUInt32(value, 0);
                        ifdqueue.Add(interopifdpointer, IFD.Interop);
                    }

                    // Save the offset to maker note data
                    if (currentifd == IFD.EXIF && tag == 37500)
                        makerNoteOffset = conv.ToUInt32(value, 0);

                    // Calculate the bytes we need to read
                    uint baselength = 0;
                    if (type == 1 || type == 2 || type == 7)
                        baselength = 1;
                    else if (type == 3)
                        baselength = 2;
                    else if (type == 4 || type == 9)
                        baselength = 4;
                    else if (type == 5 || type == 10)
                        baselength = 8;
                    uint totallength = count * baselength;

                    // If field value does not fit in 4 bytes
                    // the value field is an offset to the actual
                    // field value
                    int fieldposition = 0;
                    if (totallength > 4)
                    {
                        fieldposition = tiffoffset + (int)conv.ToUInt32(value, 0);
                        value = new byte[totallength];
                        Array.Copy(header, fieldposition, value, 0, totallength);
                    }

                    // Compressed thumbnail data
                    if (currentifd == IFD.First && tag == 0x201)
                    {
                        thumbtype = 0;
                        thumboffset = (int)conv.ToUInt32(value, 0);
                    }
                    else if (currentifd == IFD.First && tag == 0x202)
                        thumblength = (int)conv.ToUInt32(value, 0);

                    // Uncompressed thumbnail data
                    if (currentifd == IFD.First && tag == 0x111)
                    {
                        thumbtype = 1;
                        // Offset to first strip
                        if (type == 3)
                            thumboffset = (int)conv.ToUInt16(value, 0);
                        else
                            thumboffset = (int)conv.ToUInt32(value, 0);
                    }
                    else if (currentifd == IFD.First && tag == 0x117)
                    {
                        thumblength = 0;
                        for (int j = 0; j < count; j++)
                        {
                            if (type == 3)
                                thumblength += (int)conv.ToUInt16(value, 0);
                            else
                                thumblength += (int)conv.ToUInt32(value, 0);
                        }
                    }

                    // Create the exif property from the interop data
                    ExifProperty prop = ExifPropertyFactory.Get(tag, type, count, value, ByteOrder, currentifd);
                    Properties.Add(prop);
                }

                // 1st IFD pointer
                int firstifdpointer = (int)conv.ToUInt32(header, ifdoffset + 2 + 12 * fieldcount);
                if (firstifdpointer != 0)
                    ifdqueue.Add(firstifdpointer, IFD.First);
                // Read thumbnail
                if (thumboffset != -1 && thumblength != 0 && Thumbnail == null)
                {
                    if (thumbtype == 0)
                    {
                        using (MemoryStream ts = new MemoryStream(header, tiffoffset + thumboffset, thumblength))
                        {
                            Thumbnail = new JPEGFile(ts);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Replaces the contents of the APP1 section with the Exif properties.
        /// </summary>
        private bool WriteExifApp1(bool preserveMakerNote)
        {
            // Zero out IFD field offsets. We will fill those as we write the IFD sections
            exifIFDFieldOffset = 0;
            gpsIFDFieldOffset = 0;
            interopIFDFieldOffset = 0;
            firstIFDFieldOffset = 0;
            // We also do not know the location of the embedded thumbnail yet
            thumbOffsetLocation = 0;
            thumbOffsetValue = 0;
            thumbSizeLocation = 0;
            thumbSizeValue = 0;

            // Which IFD sections do we have?
            Dictionary<ExifTag, ExifProperty> ifdzeroth = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdexif = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdgps = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdinterop = new Dictionary<ExifTag, ExifProperty>();
            Dictionary<ExifTag, ExifProperty> ifdfirst = new Dictionary<ExifTag, ExifProperty>();

            foreach (ExifProperty prop in Properties)
            {
                switch (prop.IFD)
                {
                    case IFD.Zeroth:
                        ifdzeroth.Add(prop.Tag, prop);
                        break;
                    case IFD.EXIF:
                        ifdexif.Add(prop.Tag, prop);
                        break;
                    case IFD.GPS:
                        ifdgps.Add(prop.Tag, prop);
                        break;
                    case IFD.Interop:
                        ifdinterop.Add(prop.Tag, prop);
                        break;
                    case IFD.First:
                        ifdfirst.Add(prop.Tag, prop);
                        break;
                }
            }

            // Add IFD pointers if they are missing
            // We will write the pointer values later on
            if (ifdexif.Count != 0 && !ifdzeroth.ContainsKey(ExifTag.EXIFIFDPointer))
                ifdzeroth.Add(ExifTag.EXIFIFDPointer, new ExifUInt(ExifTag.EXIFIFDPointer, 0));
            if (ifdgps.Count != 0 && !ifdzeroth.ContainsKey(ExifTag.GPSIFDPointer))
                ifdzeroth.Add(ExifTag.GPSIFDPointer, new ExifUInt(ExifTag.GPSIFDPointer, 0));
            if (ifdinterop.Count != 0 && !ifdexif.ContainsKey(ExifTag.InteroperabilityIFDPointer))
                ifdexif.Add(ExifTag.InteroperabilityIFDPointer, new ExifUInt(ExifTag.InteroperabilityIFDPointer, 0));

            // Remove IFD pointers if IFD sections are missing
            if (ifdexif.Count == 0 && ifdzeroth.ContainsKey(ExifTag.EXIFIFDPointer))
                ifdzeroth.Remove(ExifTag.EXIFIFDPointer);
            if (ifdgps.Count == 0 && ifdzeroth.ContainsKey(ExifTag.GPSIFDPointer))
                ifdzeroth.Remove(ExifTag.GPSIFDPointer);
            if (ifdinterop.Count == 0 && ifdexif.ContainsKey(ExifTag.InteroperabilityIFDPointer))
                ifdexif.Remove(ExifTag.InteroperabilityIFDPointer);

            if (ifdzeroth.Count == 0 && ifdgps.Count == 0 && ifdinterop.Count == 0 && ifdfirst.Count == 0)
            {
                // Nothing to write
                return false;
            }

            // We will need these bitconverters to write byte-ordered data
            BitConverterEx bceJPEG = new BitConverterEx(BitConverterEx.ByteOrder.System, BitConverterEx.ByteOrder.BigEndian);
            BitConverterEx bceExif = new BitConverterEx(BitConverterEx.ByteOrder.System, ByteOrder);

            // Create a memory stream to write the APP1 section to
            MemoryStream ms = new MemoryStream();

            // Exif identifer
            ms.Write(Encoding.ASCII.GetBytes("Exif\0\0"), 0, 6);

            // TIFF header
            // Byte order
            long tiffoffset = ms.Position;
            ms.Write((ByteOrder == BitConverterEx.ByteOrder.LittleEndian ? new byte[] { 0x49, 0x49 } : new byte[] { 0x4D, 0x4D }), 0, 2);
            // TIFF ID
            ms.Write(bceExif.GetBytes((ushort)42), 0, 2);
            // Offset to 0th IFD
            ms.Write(bceExif.GetBytes((uint)8), 0, 4);

            // Write IFDs
            WriteIFD(ms, ifdzeroth, IFD.Zeroth, tiffoffset, preserveMakerNote);
            uint exififdrelativeoffset = (uint)(ms.Position - tiffoffset);
            WriteIFD(ms, ifdexif, IFD.EXIF, tiffoffset, preserveMakerNote);
            uint gpsifdrelativeoffset = (uint)(ms.Position - tiffoffset);
            WriteIFD(ms, ifdgps, IFD.GPS, tiffoffset, preserveMakerNote);
            uint interopifdrelativeoffset = (uint)(ms.Position - tiffoffset);
            WriteIFD(ms, ifdinterop, IFD.Interop, tiffoffset, preserveMakerNote);
            uint firstifdrelativeoffset = (uint)(ms.Position - tiffoffset);
            WriteIFD(ms, ifdfirst, IFD.First, tiffoffset, preserveMakerNote);

            // Now that we now the location of IFDs we can go back and write IFD offsets
            if (exifIFDFieldOffset != 0)
            {
                ms.Seek(exifIFDFieldOffset, SeekOrigin.Begin);
                ms.Write(bceExif.GetBytes(exififdrelativeoffset), 0, 4);
            }
            if (gpsIFDFieldOffset != 0)
            {
                ms.Seek(gpsIFDFieldOffset, SeekOrigin.Begin);
                ms.Write(bceExif.GetBytes(gpsifdrelativeoffset), 0, 4);
            }
            if (interopIFDFieldOffset != 0)
            {
                ms.Seek(interopIFDFieldOffset, SeekOrigin.Begin);
                ms.Write(bceExif.GetBytes(interopifdrelativeoffset), 0, 4);
            }
            if (firstIFDFieldOffset != 0)
            {
                ms.Seek(firstIFDFieldOffset, SeekOrigin.Begin);
                ms.Write(bceExif.GetBytes(firstifdrelativeoffset), 0, 4);
            }
            // We can write thumbnail location now
            if (thumbOffsetLocation != 0)
            {
                ms.Seek(thumbOffsetLocation, SeekOrigin.Begin);
                ms.Write(bceExif.GetBytes(thumbOffsetValue), 0, 4);
            }
            if (thumbSizeLocation != 0)
            {
                ms.Seek(thumbSizeLocation, SeekOrigin.Begin);
                ms.Write(bceExif.GetBytes(thumbSizeValue), 0, 4);
            }

            ms.Close();

            // Return APP1 header
            exifApp1.Header = ms.ToArray();
            return true;
        }

        private void WriteIFD(MemoryStream stream, Dictionary<ExifTag, ExifProperty> ifd, IFD ifdtype, long tiffoffset, bool preserveMakerNote)
        {
            BitConverterEx conv = new BitConverterEx(BitConverterEx.ByteOrder.System, ByteOrder);

            // Create a queue of fields to write
            Queue<ExifProperty> fieldqueue = new Queue<ExifProperty>();
            foreach (ExifProperty prop in ifd.Values)
                if (prop.Tag != ExifTag.MakerNote)
                    fieldqueue.Enqueue(prop);
            // Push the maker note data to the end
            if (ifd.ContainsKey(ExifTag.MakerNote))
                fieldqueue.Enqueue(ifd[ExifTag.MakerNote]);

            // Offset to start of field data from start of TIFF header
            uint dataoffset = (uint)(2 + ifd.Count * 12 + 4 + stream.Position - tiffoffset);
            uint currentdataoffset = dataoffset;
            long absolutedataoffset = stream.Position + (2 + ifd.Count * 12 + 4);

            bool makernotewritten = false;
            // Field count
            stream.Write(conv.GetBytes((ushort)ifd.Count), 0, 2);
            // Fields
            while (fieldqueue.Count != 0)
            {
                ExifProperty field = fieldqueue.Dequeue();
                ExifInterOperability interop = field.Interoperability;

                uint fillerbytecount = 0;

                // Try to preserve the makernote data offset
                if (!makernotewritten &&
                    !makerNoteProcessed &&
                    makerNoteOffset != 0 &&
                    ifdtype == IFD.EXIF &&
                    field.Tag != ExifTag.MakerNote &&
                    interop.Data.Length > 4 &&
                    currentdataoffset + interop.Data.Length > makerNoteOffset &&
                    ifd.ContainsKey(ExifTag.MakerNote))
                {
                    // Delay writing this field until we write makernote data
                    fieldqueue.Enqueue(field);
                    continue;
                }
                else if (field.Tag == ExifTag.MakerNote)
                {
                    makernotewritten = true;
                    // We may need to write filler bytes to preserve maker note offset
                    if (preserveMakerNote && !makerNoteProcessed)
                        fillerbytecount = makerNoteOffset - currentdataoffset;
                    else
                        fillerbytecount = 0;
                }

                // Tag
                stream.Write(conv.GetBytes(interop.TagID), 0, 2);
                // Type
                stream.Write(conv.GetBytes(interop.TypeID), 0, 2);
                // Count
                stream.Write(conv.GetBytes(interop.Count), 0, 4);
                // Field data
                byte[] data = interop.Data;
                if (ByteOrder != BitConverterEx.SystemByteOrder)
                {
                    if (interop.TypeID == 1 || interop.TypeID == 3 || interop.TypeID == 4 || interop.TypeID == 9)
                        Array.Reverse(data);
                    else if (interop.TypeID == 5 || interop.TypeID == 10)
                    {
                        Array.Reverse(data, 0, 4);
                        Array.Reverse(data, 4, 4);
                    }
                }

                // Fields containing offsets to other IFDs
                // Just store their offets, we will write the values later on when we know the lengths of IFDs
                if (ifdtype == IFD.Zeroth && interop.TagID == 0x8769)
                    exifIFDFieldOffset = stream.Position;
                else if (ifdtype == IFD.Zeroth && interop.TagID == 0x8825)
                    gpsIFDFieldOffset = stream.Position;
                else if (ifdtype == IFD.EXIF && interop.TagID == 0xa005)
                    interopIFDFieldOffset = stream.Position;
                else if (ifdtype == IFD.First && interop.TagID == 0x201)
                    thumbOffsetLocation = stream.Position;
                else if (ifdtype == IFD.First && interop.TagID == 0x202)
                    thumbSizeLocation = stream.Position;

                // Write 4 byte field value or field data
                if (data.Length <= 4)
                {
                    stream.Write(data, 0, data.Length);
                    for (int i = data.Length; i < 4; i++)
                        stream.WriteByte(0);
                }
                else
                {
                    // Pointer to data area relative to TIFF header
                    stream.Write(conv.GetBytes(currentdataoffset + fillerbytecount), 0, 4);
                    // Actual data
                    long currentoffset = stream.Position;
                    stream.Seek(absolutedataoffset, SeekOrigin.Begin);
                    // Write filler bytes
                    for (int i = 0; i < fillerbytecount; i++)
                        stream.WriteByte(0xFF);
                    stream.Write(data, 0, data.Length);
                    stream.Seek(currentoffset, SeekOrigin.Begin);
                    // Increment pointers
                    currentdataoffset += fillerbytecount + (uint)data.Length;
                    absolutedataoffset += fillerbytecount + data.Length;
                }
            }
            // Offset to 1st IFD
            // We will write zeros for now. This will be filled after we write all IFDs
            if (ifdtype == IFD.Zeroth)
                firstIFDFieldOffset = stream.Position;
            stream.Write(new byte[] { 0, 0, 0, 0 }, 0, 4);

            // Seek to end of IFD
            stream.Seek(absolutedataoffset, SeekOrigin.Begin);

            // Write thumbnail data
            if (ifdtype == IFD.First)
            {
                if (Thumbnail != null)
                {
                    MemoryStream ts = new MemoryStream();
                    Thumbnail.Save(ts);
                    ts.Close();
                    byte[] thumb = ts.ToArray();
                    thumbOffsetValue = (uint)(stream.Position - tiffoffset);
                    thumbSizeValue = (uint)thumb.Length;
                    stream.Write(thumb, 0, thumb.Length);
                    ts.Dispose();
                }
                else
                {
                    thumbOffsetValue = 0;
                    thumbSizeValue = 0;
                }
            }
        }
        #endregion

        #region Static Helper Methods
        /// <summary>
        /// Creates a new ExifFile from the given JPEG/Exif image file.
        /// </summary>
        /// <param name="filename">Path to the JPEJ/Exif image file.</param>
        /// <returns>An ExifFile class initialized from the specified JPEG/Exif image file.</returns>
        public static ExifFile Read(string filename)
        {
            ExifFile exif = null;
            using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read))
            {
                exif = Read(stream);
            }
            return exif;
        }
        /// <summary>
        /// Creates a new ExifFile from the given stream file.
        /// </summary>
        /// <param name="stream">A stream containing an JPEJ/Exif image.</param>
        /// <returns>An ExifFile class initialized from the specified JPEG/Exif image stream.</returns>
        public static ExifFile Read(Stream stream)
        {
            ExifFile exif = new ExifFile();

            // Read the JPEG file and process sections
            exif.Properties = new ExifPropertyCollection();
            exif.file = new JPEGFile(stream);

            // Read metadata sections
            exif.ReadJFIFAPP0();
            exif.ReadJFXXAPP0();
            exif.ReadExifAPP1();

            // Process the maker note
            exif.makerNoteProcessed = false;
            /*
            // Assign the custom type descriptor
            TypeDescriptor.AddProvider(
                new ExifFileTypeDescriptionProvider(
                    TypeDescriptor.GetProvider(typeof(ExifFile))), exif);
             * */
            /*
            PropertyDescriptorCollection properties = TypeDescriptor.GetProperties(exif);
            foreach (PropertyDescriptor property in properties) 
                Console.WriteLine("- " + property.Name);
            */
            return exif;
        }
        #endregion
    }
}