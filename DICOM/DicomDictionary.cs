﻿// Copyright (c) 2012-2015 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Concurrent;

namespace Dicom
{
    using System;
    using System.Collections.Generic;
    using System.IO.Compression;
    using System.Linq;
    using System.Reflection;

    using Dicom.IO;

    /// <summary>
    /// Class for managing DICOM dictionaries.
    /// </summary>
    public class DicomDictionary : IEnumerable<DicomDictionaryEntry>
    {
        #region Private Members

        public static readonly DicomDictionaryEntry UnknownTag =
            new DicomDictionaryEntry(
                DicomMaskedTag.Parse("xxxx", "xxxx"),
                "Unknown",
                "Unknown",
                DicomVM.VM_1_n,
                false,
                DicomVR.UN,
                DicomVR.AE,
                DicomVR.AS,
                DicomVR.AT,
                DicomVR.CS,
                DicomVR.DA,
                DicomVR.DS,
                DicomVR.DT,
                DicomVR.FD,
                DicomVR.FL,
                DicomVR.IS,
                DicomVR.LO,
                DicomVR.LT,
                DicomVR.OB,
                DicomVR.OF,
                DicomVR.OW,
                DicomVR.PN,
                DicomVR.SH,
                DicomVR.SL,
                DicomVR.SQ,
                DicomVR.SS,
                DicomVR.ST,
                DicomVR.TM,
                DicomVR.UI,
                DicomVR.UL,
                DicomVR.US,
                DicomVR.UT);

        public static readonly DicomDictionaryEntry PrivateCreatorTag =
            new DicomDictionaryEntry(
                DicomMaskedTag.Parse("xxxx", "00xx"),
                "Private Creator",
                "PrivateCreator",
                DicomVM.VM_1,
                false,
                DicomVR.LO);

        private DicomPrivateCreator _privateCreator;

        private ConcurrentDictionary<string, DicomPrivateCreator> _creators;

        private ConcurrentDictionary<DicomPrivateCreator, DicomDictionary> _private;

        private ConcurrentDictionary<DicomTag, DicomDictionaryEntry> _entries;

        private object _maskedLock;
        private List<DicomDictionaryEntry> _masked;

        private bool _maskedNeedsSort;

        #endregion

        #region Constructors

        public DicomDictionary()
        {
            _creators = new ConcurrentDictionary<string, DicomPrivateCreator>();
            _private = new ConcurrentDictionary<DicomPrivateCreator, DicomDictionary>();
            _entries = new ConcurrentDictionary<DicomTag, DicomDictionaryEntry>();
            _masked = new List<DicomDictionaryEntry>();
            _maskedLock = new object();
            _maskedNeedsSort = false;
        }

        private DicomDictionary(DicomPrivateCreator creator)
        {
            _privateCreator = creator;
            _entries = new ConcurrentDictionary<DicomTag, DicomDictionaryEntry>();
            _masked = new List<DicomDictionaryEntry>();
            _maskedLock = new object();
            _maskedNeedsSort = false;
        }

        #endregion

        #region Properties

        private static readonly object _lock = new object();

        private static DicomDictionary _default;
        private static bool _defaultIncludesPrivate;

        /// <summary>
        /// Ensures the default DICOM dictionaries are loaded
        /// Safe to call multiple times but will throw an exception if inconsistent values for loadPrivateDictionary are provided over multiple calls
        /// </summary>
        /// <param name="loadPrivateDictionary">Leave null (default value) if unconcerned.  Set true to search for resource streams named "Dicom.Dictionaries.Private Dictionary.xml.gz" in referenced assemblies</param>
        /// <returns></returns>
        public static DicomDictionary EnsureDefaultDictionariesLoaded(bool? loadPrivateDictionary = null)
        {
            // short-circuit if already initialised (#151).
            if (_default != null)
            {
                if (loadPrivateDictionary.HasValue && _defaultIncludesPrivate != loadPrivateDictionary.Value)
                {
                    throw new DicomDataException("Default DICOM dictionary already loaded " +
                                                 (_defaultIncludesPrivate ? "with" : "without") +
                                                 "private dictionary and the current request to ensure the default dictionary is loaded requests that private dictionary " +
                                                 (loadPrivateDictionary.Value ? "is" : "is not") + " loaded");
                }
                return _default;
            }

            lock (_lock)
            {
                if (_default == null)
                {
                    var dict = new DicomDictionary();
                    dict.Add(
                        new DicomDictionaryEntry(
                            DicomMaskedTag.Parse("xxxx", "0000"),
                            "Group Length",
                            "GroupLength",
                            DicomVM.VM_1,
                            false,
                            DicomVR.UL));
                    try
                    {
                        var assembly = typeof (DicomDictionary).GetTypeInfo().Assembly;
                        var stream = assembly.GetManifestResourceStream("Dicom.Dictionaries.DICOM Dictionary.xml.gz");
                        var gzip = new GZipStream(stream, CompressionMode.Decompress);
                        var reader = new DicomDictionaryReader(dict, DicomDictionaryFormat.XML, gzip);
                        reader.Process();
                    }
                    catch (Exception e)
                    {
                        throw new DicomDataException(
                            "Unable to load DICOM dictionary from resources.\n\n" + e.Message,
                            e);
                    }
                    if (loadPrivateDictionary.GetValueOrDefault(true))
                    {
                        try
                        {
                            var assembly = typeof (DicomDictionary).GetTypeInfo().Assembly;
                            var stream =
                                assembly.GetManifestResourceStream("Dicom.Dictionaries.Private Dictionary.xml.gz");
                            var gzip = new GZipStream(stream, CompressionMode.Decompress);
                            var reader = new DicomDictionaryReader(dict, DicomDictionaryFormat.XML, gzip);
                            reader.Process();
                        }
                        catch (Exception e)
                        {
                            throw new DicomDataException(
                                "Unable to load private dictionary from resources.\n\n" + e.Message,
                                e);
                        }
                    }

                    _defaultIncludesPrivate = loadPrivateDictionary.GetValueOrDefault(true);
                    _default = dict;
                }
                else
                {
                    //ensure the race wasn't for two different "load private dictionary" states
                    if (loadPrivateDictionary.HasValue && _defaultIncludesPrivate != loadPrivateDictionary)
                    {
                        throw new DicomDataException("Default DICOM dictionary already loaded " +
                                                     (_defaultIncludesPrivate ? "with" : "without") +
                                                     "private dictionary and the current request to ensure the default dictionary is loaded requests that private dictionary " +
                                                     (loadPrivateDictionary.Value ? "is" : "is not") + " loaded");
                    }
                    return _default;
                }

                //race is complete
                return _default;
            }
        }

        public static DicomDictionary Default
        {
            get
            {
                return EnsureDefaultDictionariesLoaded(loadPrivateDictionary: null);
            }
            set
            {
                lock (_lock)
                {
                    if (_default != null)
                    {
                        throw new DicomDataException(
                            "Cannot set Default DicomDictionary as it has already been initialised");
                    }
                    _default = value;
                }
            }
        }

        public DicomPrivateCreator PrivateCreator
        {
            get
            {
                return _privateCreator;
            }
            internal set
            {
                _privateCreator = value;
            }
        }

        public DicomDictionaryEntry this[DicomTag tag]
        {
            get
            {
                if (_private != null && tag.PrivateCreator != null)
                {
                    DicomDictionary pvt = null;
                    if (_private.TryGetValue(tag.PrivateCreator, out pvt)) return pvt[tag];
                }

                // special case for private creator tag
                if (tag.IsPrivate && tag.Element != 0x0000 && tag.Element <= 0x00ff) return PrivateCreatorTag;

                DicomDictionaryEntry entry = null;
                if (_entries.TryGetValue(tag, out entry)) return entry;

                // this is faster than LINQ query
                lock (_maskedLock)
                {
                    foreach (var x in _masked)
                    {
                        if (x.MaskTag.IsMatch(tag)) return x;
                    }
                }

                return UnknownTag;
            }
        }

        public DicomDictionary this[DicomPrivateCreator creator]
        {
            get
            {
                return _private.GetOrAdd(creator, _ => new DicomDictionary(creator));
            }
        }

        #endregion

        #region Public Methods

        public void Add(DicomDictionaryEntry entry)
        {
            if (_privateCreator != null)
            {
                entry.Tag = new DicomTag(entry.Tag.Group, entry.Tag.Element, _privateCreator);
                if (entry.MaskTag != null) entry.MaskTag.Tag = entry.Tag;
            }

            if (entry.MaskTag == null)
            {
                // allow overwriting of existing entries
                _entries[entry.Tag] = entry;
            }
            else
            {
                lock (_maskedLock)
                {
                    _masked.Add(entry);
                    _maskedNeedsSort = true;
                }
            }
        }

        public DicomPrivateCreator GetPrivateCreator(string creator)
        {
            return _creators.GetOrAdd(creator, _ => new DicomPrivateCreator(creator));
        }

        /// <summary>
        /// Load DICOM dictionary data from file.
        /// </summary>
        /// <param name="file">File name.</param>
        /// <param name="format">File format.</param>
        public void Load(string file, DicomDictionaryFormat format)
        {
            using (var fs = IOManager.CreateFileReference(file).OpenRead())
            {
                var s = fs;
                if (file.EndsWith(".gz"))
                {
                    s = new GZipStream(s, CompressionMode.Decompress);
                }

                var reader = new DicomDictionaryReader(this, format, s);
                reader.Process();
            }
        }

        #endregion

        #region IEnumerable Members

        public IEnumerator<DicomDictionaryEntry> GetEnumerator()
        {
            List<DicomDictionaryEntry> items = new List<DicomDictionaryEntry>();
            items.AddRange(_entries.Values.OrderBy(x => x.Tag));

            lock (_maskedLock)
            {
                if (_maskedNeedsSort)
                {
                    _masked.Sort((a, b) => a.MaskTag.Mask.CompareTo(b.MaskTag.Mask));
                    _maskedNeedsSort = false;
                }
                items.AddRange(_masked);
            }
            return items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }
}
