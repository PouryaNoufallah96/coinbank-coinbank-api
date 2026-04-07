namespace CoinBank.Services._BlockChain.DTOs.Settings
{

    public static class TokenForwardSaleAbi
    {
        public const string Value = @"
   [
    {
        ""type"": ""function"",
        ""name"": ""batchLiquidateInsurances"",
        ""inputs"": [
            {
                ""name"": ""insuranceIds"",
                ""type"": ""bytes32[]"",
                ""internalType"": ""bytes32[]""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""finalizeInsurance"",
        ""inputs"": [
            {
                ""name"": ""insuranceId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""getInsurance"",
        ""inputs"": [
            {
                ""name"": ""insuranceId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": """",
                ""type"": ""tuple"",
                ""internalType"": ""struct ShieldStorage.Insurance"",
                ""components"": [
                    {
                        ""name"": ""payoutAmount"",
                        ""type"": ""uint128"",
                        ""internalType"": ""uint128""
                    },
                    {
                        ""name"": ""payoutAmountInUsd"",
                        ""type"": ""uint128"",
                        ""internalType"": ""uint128""
                    },
                    {
                        ""name"": ""coverageAmount"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""initalPrice"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""finalPrice"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""startDate"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""endDate"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""insuredToken"",
                        ""type"": ""address"",
                        ""internalType"": ""address""
                    },
                    {
                        ""name"": ""user"",
                        ""type"": ""address"",
                        ""internalType"": ""address""
                    },
                    {
                        ""name"": ""insuranceType"",
                        ""type"": ""uint8"",
                        ""internalType"": ""enum ShieldStorage.InsuranceType""
                    },
                    {
                        ""name"": ""status"",
                        ""type"": ""uint8"",
                        ""internalType"": ""enum ShieldStorage.InsuranceStatus""
                    }
                ]
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""insurances"",
        ""inputs"": [
            {
                ""name"": """",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [
            {
                ""name"": ""payoutAmount"",
                ""type"": ""uint128"",
                ""internalType"": ""uint128""
            },
            {
                ""name"": ""payoutAmountInUsd"",
                ""type"": ""uint128"",
                ""internalType"": ""uint128""
            },
            {
                ""name"": ""coverageAmount"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""initalPrice"",
                ""type"": ""uint64"",
                ""internalType"": ""uint64""
            },
            {
                ""name"": ""finalPrice"",
                ""type"": ""uint64"",
                ""internalType"": ""uint64""
            },
            {
                ""name"": ""startDate"",
                ""type"": ""uint64"",
                ""internalType"": ""uint64""
            },
            {
                ""name"": ""endDate"",
                ""type"": ""uint64"",
                ""internalType"": ""uint64""
            },
            {
                ""name"": ""insuredToken"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""user"",
                ""type"": ""address"",
                ""internalType"": ""address""
            },
            {
                ""name"": ""insuranceType"",
                ""type"": ""uint8"",
                ""internalType"": ""enum ShieldStorage.InsuranceType""
            },
            {
                ""name"": ""status"",
                ""type"": ""uint8"",
                ""internalType"": ""enum ShieldStorage.InsuranceStatus""
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""function"",
        ""name"": ""insureToken"",
        ""inputs"": [
            {
                ""name"": ""params"",
                ""type"": ""tuple"",
                ""internalType"": ""struct ShieldStorage.RegisterInsuranceParams"",
                ""components"": [
                    {
                        ""name"": ""payoutAmount"",
                        ""type"": ""uint128"",
                        ""internalType"": ""uint128""
                    },
                    {
                        ""name"": ""payoutAmountInUsd"",
                        ""type"": ""uint128"",
                        ""internalType"": ""uint128""
                    },
                    {
                        ""name"": ""coverageAmount"",
                        ""type"": ""uint256"",
                        ""internalType"": ""uint256""
                    },
                    {
                        ""name"": ""startDate"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""endDate"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""initalPrice"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""sigDeadline"",
                        ""type"": ""uint64"",
                        ""internalType"": ""uint64""
                    },
                    {
                        ""name"": ""insuredToken"",
                        ""type"": ""address"",
                        ""internalType"": ""address""
                    },
                    {
                        ""name"": ""user"",
                        ""type"": ""address"",
                        ""internalType"": ""address""
                    },
                    {
                        ""name"": ""insuranceType"",
                        ""type"": ""uint8"",
                        ""internalType"": ""enum ShieldStorage.InsuranceType""
                    }
                ]
            },
            {
                ""name"": ""signature"",
                ""type"": ""bytes"",
                ""internalType"": ""bytes""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""liquidateInsurance"",
        ""inputs"": [
            {
                ""name"": ""insuranceId"",
                ""type"": ""bytes32"",
                ""internalType"": ""bytes32""
            }
        ],
        ""outputs"": [],
        ""stateMutability"": ""nonpayable""
    },
    {
        ""type"": ""function"",
        ""name"": ""nonces"",
        ""inputs"": [
            {
                ""name"": ""user"",
                ""type"": ""address"",
                ""internalType"": ""address""
            }
        ],
        ""outputs"": [
            {
                ""name"": ""nonce"",
                ""type"": ""uint256"",
                ""internalType"": ""uint256""
            }
        ],
        ""stateMutability"": ""view""
    },
    {
        ""type"": ""event"",
        ""name"": ""InsuranceCancelled"",
        ""inputs"": [
            {
                ""name"": ""insuranceId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""user"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            }
        ],
        ""anonymous"": false
    },
    {
        ""type"": ""event"",
        ""name"": ""InsuranceFinalized"",
        ""inputs"": [
            {
                ""name"": ""insuranceId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""user"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""settlementAmount"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""finalPrice"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            },
            {
                ""name"": ""payoutToken"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""payoutAmount"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            }
        ],
        ""anonymous"": false
    },
    {
        ""type"": ""event"",
        ""name"": ""InsuranceRegistered"",
        ""inputs"": [
            {
                ""name"": ""insuranceId"",
                ""type"": ""bytes32"",
                ""indexed"": false,
                ""internalType"": ""bytes32""
            },
            {
                ""name"": ""user"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""insuredToken"",
                ""type"": ""address"",
                ""indexed"": false,
                ""internalType"": ""address""
            },
            {
                ""name"": ""coverageAmount"",
                ""type"": ""uint256"",
                ""indexed"": false,
                ""internalType"": ""uint256""
            }
        ],
        ""anonymous"": false
    }
]
    ";

        public const string ERC20Abi = @"[
            { 'constant':true,'inputs':[{'name':'_owner','type':'address'}],'name':'balanceOf','outputs':[{'name':'balance','type':'uint256'}],'type':'function' },
            { 'constant':true,'inputs':[],'name':'decimals','outputs':[{'name':'','type':'uint8'}],'type':'function' },
            { 'constant':true,'inputs':[],'name':'symbol','outputs':[{'name':'','type':'string'}],'type':'function' }
        ]";

    }


}
